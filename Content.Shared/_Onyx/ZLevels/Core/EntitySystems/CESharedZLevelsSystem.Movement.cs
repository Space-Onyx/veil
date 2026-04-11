/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Content.Shared._Onyx.ZLevels.Core.Components;
using Content.Shared.CCVar;
using Content.Shared.Chasm;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Gravity;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Maps;
using Content.Shared.Movement.Components;
using Content.Shared.Throwing;
using JetBrains.Annotations;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Shared._Onyx.ZLevels.Core.EntitySystems;

public abstract partial class CESharedZLevelsSystem
{
    public const int MaxZLevelsBelowRendering = 3;

    private const float ZGravityForce = 9.8f;
    private const float ZVelocityLimit = 20.0f;

    /// <summary>
    /// The maximum height at which a player will automatically climb higher when stepping on a highground entity.
    /// </summary>
    private const float MaxStepHeight = 0.5f;

    /// <summary>
    /// The minimum speed required to trigger LandEvent events.
    /// </summary>
    private const float ImpactVelocityLimit = 0.75f;

    private EntityQuery<CEZLevelHighGroundComponent> _highgroundQuery;
    private List<(EntityUid Uid, float Velocity)> _queuedLandings = new();   // ECHO-Tweak, Onyx-Tweak: List instead of Dictionary to avoid O(n2) ElementAt

    private readonly Dictionary<EntityUid, uint> _groundCacheGenerationByMap = new();
    private uint _groundCacheGenerationCounter = 1;

    private readonly record struct UpperGridCoverageCacheEntry(
        uint LowerMapGeneration,
        uint UpperMapGeneration,
        HashSet<Vector2i> Coverage);

    private readonly record struct UpperGridInteriorHoleCacheEntry(
        uint UpperMapGeneration,
        HashSet<Vector2i> Holes);

    private uint GetGroundCacheGenerationForMap(EntityUid mapUid)
    {
        if (_groundCacheGenerationByMap.TryGetValue(mapUid, out var cachedGeneration))
            return cachedGeneration;

        _groundCacheGenerationByMap[mapUid] = _groundCacheGenerationCounter;
        return _groundCacheGenerationCounter;
    }

    private uint NextGroundCacheGeneration()
    {
        _groundCacheGenerationCounter++;
        if (_groundCacheGenerationCounter != 0)
            return _groundCacheGenerationCounter;

        _groundCacheGenerationCounter = 1;
        _groundCacheGenerationByMap.Clear();
        _upperGridCoverageCache.Clear();
        _upperGridInteriorHolesCache.Clear();
        _mapGravityCache.Clear();
        return _groundCacheGenerationCounter;
    }

    private void InvalidateGroundCacheForMap(EntityUid mapUid, bool includeAdjacentMaps = false)
    {
        _groundCacheGenerationByMap[mapUid] = NextGroundCacheGeneration();

        if (_mapQuery.TryComp(mapUid, out var mapComp))
            _mapGravityCache.Remove(mapComp.MapId);

        if (!includeAdjacentMaps || !_zMapQuery.TryComp(mapUid, out var zMap))
            return;

        if (TryMapUp((mapUid, zMap), out var mapAbove))
        {
            _groundCacheGenerationByMap[mapAbove.Value.Owner] = NextGroundCacheGeneration();

            if (_mapQuery.TryComp(mapAbove.Value.Owner, out var aboveMapComp))
                _mapGravityCache.Remove(aboveMapComp.MapId);
        }

        if (TryMapDown((mapUid, zMap), out var mapBelow))
        {
            _groundCacheGenerationByMap[mapBelow.Value.Owner] = NextGroundCacheGeneration();

            if (_mapQuery.TryComp(mapBelow.Value.Owner, out var belowMapComp))
                _mapGravityCache.Remove(belowMapComp.MapId);
        }
    }

    private bool IsGroundCacheValidForCurrentMap(CEZPhysicsComponent zPhys, TransformComponent xform)
    {
        if (!zPhys.GroundCacheValid || xform.MapUid is not { } mapUid)
            return false;

        return zPhys.GroundCacheMapUid == mapUid &&
               zPhys.GroundCacheGeneration == GetGroundCacheGenerationForMap(mapUid);
    }

    private void InvalidateAllGroundCaches()
    {
        _groundCacheGenerationByMap.Clear();
        _groundCacheGenerationCounter = 1;
        _upperGridCoverageCache.Clear();
        _upperGridInteriorHolesCache.Clear();
        _mapGravityCache.Clear();
    }

    private void InitMovement()
    {
        _highgroundQuery = GetEntityQuery<CEZLevelHighGroundComponent>();

        SubscribeLocalEvent<CEZPhysicsComponent, CEGetZVelocityEvent>(OnGetVelocity);
        SubscribeLocalEvent<CEZPhysicsComponent, CEZLevelMapMoveEvent>(OnZPhysicsMove);
        SubscribeLocalEvent<CEZPhysicsComponent, MoveEvent>(OnMoveEvent);

        SubscribeLocalEvent<DamageableComponent, CEZLevelHitEvent>(OnFallDamage);
        SubscribeLocalEvent<PhysicsComponent, CEZLevelHitEvent>(OnFallAreaImpact);

        SubscribeLocalEvent<CEZLevelHighGroundComponent, AnchorStateChangedEvent>(OnHighGroundAnchorChanged);
        SubscribeLocalEvent<CEZPhysicsComponent, IsWeightlessEvent>(OnZPhysicsWeightless);
    }

    private void OnZPhysicsWeightless(Entity<CEZPhysicsComponent> ent, ref IsWeightlessEvent args)
    {
        if (args.Handled)
            return;

        var xform = Transform(ent);

        if (xform.GridUid != null)
            return;

        if (!HasZNetworkGravity(xform))
            return;

        args.IsWeightless = false;
        args.Handled = true;
    }

    private void OnHighGroundAnchorChanged(Entity<CEZLevelHighGroundComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        var xform = Transform(ent);
        if (xform.MapUid is { } mapUid)
            InvalidateGroundCacheForMap(mapUid);

        if (xform.GridUid is not { } gridUid || TerminatingOrDeleted(gridUid))
            return;

        if (_gridQuery.TryComp(gridUid, out var grid))
        {
            var tilePos = _map.CoordinatesToTile(gridUid, grid,
                new MapCoordinates(_transform.GetWorldPosition(xform), xform.MapID));
            WakeEntitiesOnTile(gridUid, tilePos);
        }
    }

    private void WakeEntitiesOnTile(EntityUid gridUid, Vector2i tilePos)
    {
        if (!_gridQuery.TryComp(gridUid, out var grid))
            return;

        var gridXform = Transform(gridUid);
        if (gridXform.MapID == MapId.Nullspace)
            return;

        var worldPos = _map.GridTileToWorldPos(gridUid, grid, tilePos);
        var box = new Box2(worldPos, worldPos + new Vector2(1f, 1f));

        foreach (var uid in _lookup.GetEntitiesIntersecting(gridXform.MapID, box, LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (ZPhyzQuery.HasComp(uid) && !HasComp<CEActiveZPhysicsComponent>(uid))
                EnsureComp<CEActiveZPhysicsComponent>(uid);
        }
    }

    private void CacheMovement(Entity<CEZPhysicsComponent> ent)
    {
        if (ent.Comp.IgnoreHighGround)
            return;

        var xform = Transform(ent);
        ent.Comp.CurrentGroundHeight = ComputeGroundHeightInternal((ent, ent), out var sticky);
        ent.Comp.CurrentStickyGround = sticky;
        ent.Comp.GroundCacheValid = true;
        ent.Comp.GroundCacheMapUid = xform.MapUid ?? EntityUid.Invalid;
        ent.Comp.GroundCacheGeneration = xform.MapUid is { } mapUid
            ? GetGroundCacheGenerationForMap(mapUid)
            : 0;
    }

    private void OnMoveEvent(Entity<CEZPhysicsComponent> ent, ref MoveEvent args)
    {
        var moveXform = Transform(ent);
        if (moveXform.ParentUid != moveXform.MapUid && !_gridQuery.HasComp(moveXform.ParentUid))
            return;
        var groundH = ent.Comp.CurrentGroundHeight;
        var isOnHighGround = Math.Abs(groundH) > 0.001f && Math.Abs(groundH - (-1f)) > 0.001f;

        if (!isOnHighGround)
        {
            var xform = Transform(ent);
            var gridUid = xform.GridUid ?? EntityUid.Invalid;
            var tilePos = Vector2i.Zero;

            var worldPos = _transform.GetWorldPosition(xform);
            if (gridUid != EntityUid.Invalid && _gridQuery.TryComp(gridUid, out var grid))
            {
                tilePos = _map.CoordinatesToTile(gridUid, grid, new MapCoordinates(worldPos, xform.MapID));
            }
            else
            {
                tilePos = new Vector2i((int) MathF.Floor(worldPos.X), (int) MathF.Floor(worldPos.Y));
            }

            if (gridUid == ent.Comp.CachedGridUid && tilePos == ent.Comp.CachedTilePos)
                return;

            ent.Comp.CachedGridUid = gridUid;
            ent.Comp.CachedTilePos = tilePos;
        }

        var oldGround = ent.Comp.CurrentGroundHeight;
        CacheMovement(ent);

        if (!_timing.ApplyingState && Math.Abs(ent.Comp.CurrentGroundHeight - oldGround) > 0.01f)
            EnsureComp<CEActiveZPhysicsComponent>(ent);
    }

    private void OnZPhysicsMove(Entity<CEZPhysicsComponent> ent, ref CEZLevelMapMoveEvent args)
    {
        ent.Comp.CurrentZLevel = args.CurrentZLevel;
        DirtyField(ent, ent.Comp, nameof(CEZPhysicsComponent.CurrentZLevel));
        ent.Comp.CachedGridUid = EntityUid.Invalid;
        CacheMovement(ent);
    }

    private void OnGetVelocity(Entity<CEZPhysicsComponent> ent, ref CEGetZVelocityEvent args)
    {
        args.VelocityDelta -= ZGravityForce * ent.Comp.GravityMultiplier;
    }

    private void OnFallDamage(Entity<DamageableComponent> ent, ref CEZLevelHitEvent args) //TODO unhardcode
    {
        var knockdownTime = MathF.Min(args.ImpactPower * 0.25f, 5f);
        _stun.TryKnockdown(ent.Owner, TimeSpan.FromSeconds(knockdownTime));

        if (HasComp<OnyxZFallDamageImmuneComponent>(ent.Owner))
            return;

        var damageType = _proto.Index<DamageTypePrototype>("Blunt");
        var damageAmount = args.ImpactPower * 2f;

        _damage.TryChangeDamage(ent.Owner, new DamageSpecifier(damageType, damageAmount));
    }

    /// <summary>
    /// Cause AoE damage in impact point
    /// </summary>
    private void OnFallAreaImpact(Entity<PhysicsComponent> ent, ref CEZLevelHitEvent args)
    {
        var entitiesAround = _lookup.GetEntitiesInRange(ent, 0.25f, LookupFlags.Uncontained);

        foreach (var victim in entitiesAround)
        {
            if (victim == ent.Owner)
                continue;

            var knockdownTime = MathF.Min(args.ImpactPower * ent.Comp.Mass * 0.1f, 10f);
            _stun.TryKnockdown(victim, TimeSpan.FromSeconds(knockdownTime));

            var damageType = _proto.Index<DamageTypePrototype>("Blunt");
            var damageAmount = args.ImpactPower * ent.Comp.Mass * 0.15f;

            _damage.TryChangeDamage(victim, new DamageSpecifier(damageType, damageAmount));
        }
    }

    private void UpdateMovement(float frameTime) 
    {

        _queuedLandings.Clear(); // ECHO-Tweak

        var query = EntityQueryEnumerator<CEActiveZPhysicsComponent, CEZPhysicsComponent, TransformComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out _, out var zPhys, out var xform, out var physics))
        {
            // ECHO-Tweak: Removed old logic
            UpdateMovement(uid, zPhys, xform, physics, frameTime);
        }

        if (_net.IsServer)
        {
            for (var i = _queuedLandings.Count - 1; i >= 0; i--)
            {
                var landing = _queuedLandings[i];

                RaiseLocalEvent(landing.Uid, new CEZLevelHitEvent(landing.Velocity));
                var land = new LandEvent(null, true);
                RaiseLocalEvent(landing.Uid, ref land);
            }
        }
    }

    /// <summary>
    /// Returns the last cached distance to the floor.
    /// </summary>
    /// <param name="target">The entity, the distance to the floor which we calculate</param>
    /// <returns></returns>
    public float DistanceToGround(Entity<CEZPhysicsComponent?> target)
    {
        if (!Resolve(target, ref target.Comp, false))
            return 0;

        return target.Comp.LocalPosition - target.Comp.CurrentGroundHeight;
    }

    private bool HasBlockingTileOnMap(EntityUid ent, EntityUid mapUid)
    {
        if (!_mapQuery.TryComp(mapUid, out var mapComp))
            return false;

        var worldPos = _transform.GetWorldPosition(ent);
        foreach (var grid in GetCachedGrids(mapComp.MapId))
        {
            if (!_map.TryGetTileRef(grid.Owner, grid.Comp, worldPos, out var tileRef) ||
                tileRef.Tile.IsEmpty)
                continue;

            var tileDef = (ContentTileDefinition) TilDefMan[tileRef.Tile.TypeId];
            if (TileZRoof.HasZRoof(grid, tileRef.GridIndices, tileDef.HasZRoof))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks whether there is a ceiling above the specified entity (tiles on the layer above).
    /// If there are no Z-levels above, false will be returned.
    /// </summary>
    [PublicAPI]
    public bool HasTileAbove(EntityUid ent, Entity<CEZLevelMapComponent?>? currentMapUid = null)
    {
        currentMapUid ??= Transform(ent).MapUid;

        if (currentMapUid is null)
            return false;

        if (!TryMapUp(currentMapUid.Value, out var mapAboveUid))
            return false;

        return HasBlockingTileOnMap(ent, mapAboveUid.Value);
    }

    /// <summary>
    /// Checks whether there is a ceiling below the specified entity (tiles on the layer below).
    /// If there are no Z-levels below, false will be returned.
    /// </summary>
    [PublicAPI]
    public bool HasTileBelow(EntityUid ent, Entity<CEZLevelMapComponent?>? currentMapUid = null)
    {
        currentMapUid ??= Transform(ent).MapUid;

        if (currentMapUid is null)
            return false;

        if (!TryMapDown(currentMapUid.Value, out var mapBelowUid))
            return false;

        return HasBlockingTileOnMap(ent, mapBelowUid.Value);
    }

    /// <summary>
    /// Checks whether there is a ceiling above the specified entity (tiles on the layer above).
    /// If there are no Z-levels above, false will be returned.
    /// </summary>
    [PublicAPI]
    public bool HasTileAbove(Vector2i indices, Entity<CEZLevelMapComponent?> map)
    {
        if (!Resolve(map, ref map.Comp, false))
            return false;

        if (!TryMapUp(map, out var mapAboveUid))
            return false;

        if (!_mapQuery.TryComp(mapAboveUid.Value, out var mapComp)) 
            return false;

        foreach (var grid in GetCachedGrids(mapComp.MapId))
        {
            if (!_map.TryGetTileRef(grid.Owner, grid.Comp, indices, out var tileRef) ||
                tileRef.Tile.IsEmpty)
                continue;

            var tileDef = (ContentTileDefinition) TilDefMan[tileRef.Tile.TypeId];
            if (TileZRoof.HasZRoof(grid, tileRef.GridIndices, tileDef.HasZRoof))
                return true;
        }
        return false;
    }

    [PublicAPI]
    public void SetZPosition(Entity<CEZPhysicsComponent?> ent, float newPosition)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        ent.Comp.LocalPosition = newPosition;
        DirtyField(ent, ent.Comp, nameof(CEZPhysicsComponent.LocalPosition));
        if (!_timing.ApplyingState)
            EnsureComp<CEActiveZPhysicsComponent>(ent);
    }

    [PublicAPI]
    public void SetZGravity(Entity<CEZPhysicsComponent?> ent, float newGravityMultiplier)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        if (Math.Abs(ent.Comp.GravityMultiplier - newGravityMultiplier) < 0.001f)
            return;

        ent.Comp.GravityMultiplier = newGravityMultiplier;
        DirtyField(ent, ent.Comp, nameof(CEZPhysicsComponent.GravityMultiplier));

        if (newGravityMultiplier > 0f && !_timing.ApplyingState)
            EnsureComp<CEActiveZPhysicsComponent>(ent);
    }

    /// <summary>
    /// Sets the vertical velocity for the entity. Positive values make the entity fly upward. Negative values make it fly downward.
    /// </summary>
    [PublicAPI]
    public void SetZVelocity(Entity<CEZPhysicsComponent?> ent, float newVelocity)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        ent.Comp.Velocity = newVelocity;
        DirtyField(ent, ent.Comp, nameof(CEZPhysicsComponent.Velocity));
        EnsureComp<CEActiveZPhysicsComponent>(ent);
    }

    /// <summary>
    /// Add the vertical velocity for the entity. Positive values make the entity fly upward. Negative values make it fly downward.
    /// </summary>
    [PublicAPI]
    public void AddZVelocity(Entity<CEZPhysicsComponent?> ent, float newVelocity)
    {
        if (!Resolve(ent.Owner, ref ent.Comp, false))
            return;

        ent.Comp.Velocity += newVelocity;
        DirtyField(ent, ent.Comp, nameof(CEZPhysicsComponent.Velocity));
        EnsureComp<CEActiveZPhysicsComponent>(ent);
    }

    [PublicAPI]
    public bool TryMove(EntityUid ent, int offset, Entity<CEZLevelMapComponent?>? map = null)
    {
        map ??= Transform(ent).MapUid;

        if (map is null)
            return false;

        if (!TryMapOffset(map.Value, offset, out var targetMap))
            return false;

        if (!_mapQuery.TryComp(targetMap, out var targetMapComp))
            return false;

        var beforeEv = new CEZLevelBeforeMapMoveEvent(offset, targetMap.Value.Comp.Depth);
        RaiseLocalEvent(ent, ref beforeEv);

        _transform.SetMapCoordinates(ent, new MapCoordinates(_transform.GetWorldPosition(ent), targetMapComp.MapId));

        // Clear saved rotation so subsequent non-Z-level map changes reset normally.

        var ev = new CEZLevelMapMoveEvent(offset, targetMap.Value.Comp.Depth);
        RaiseLocalEvent(ent, ev);

        return true;
    }

    [PublicAPI]
    public bool TryMoveUp(EntityUid ent)
    {
        return TryMove(ent, 1);
    }

    [PublicAPI]
    public bool TryMoveDown(EntityUid ent)
    {
        return TryMove(ent, -1);
    }

    [PublicAPI]
    public bool TryMoveDownOrChasm(EntityUid ent)
    {
        if (TryMoveDown(ent))
            return true;

        if (IsOpenSpaceAtCurrentLevel(ent))
            return false;

        if (!Cfg.GetCVar(CCVars.ZLevelChasmFallEnabled))
            return false;
        if (HasComp<ChasmFallingComponent>(ent))
            return false; //Already falling

        var audio = new SoundPathSpecifier("/Audio/Effects/falling.ogg");
        _audio.PlayPredicted(audio, Transform(ent).Coordinates, ent);
        var falling = AddComp<ChasmFallingComponent>(ent);
        falling.NextDeletionTime = _timing.CurTime + falling.DeletionTime;
        _blocker.UpdateCanMove(ent);

        return false;
    }

    private bool IsOpenSpaceAtCurrentLevel(EntityUid ent)
    {
        var xform = Transform(ent);
        if (xform.MapUid is not { } mapUid ||
            !_zMapQuery.TryComp(mapUid, out var zMap) ||
            !_mapQuery.TryComp(mapUid, out var mapComp))
            return false;

        if (TryMapDown((mapUid, zMap), out _))
            return false;

        var worldPos = _transform.GetWorldPosition(ent);
        foreach (var grid in GetCachedGrids(mapComp.MapId))
        {
            if (!_map.TryGetTileRef(grid.Owner, grid.Comp, worldPos, out var tileRef))
                continue;

            if (tileRef.Tile.IsEmpty)
                continue;

            var tileDef = (ContentTileDefinition) TilDefMan[tileRef.Tile.TypeId];
            if (!tileDef.MapAtmosphere)
                return false;
        }

        return true;
    }
}

/// <summary>
/// Is called on an entity right before it moves between z-levels.
/// </summary>
/// <param name="offset">How many levels were crossed. If negative, it means there was a downward movement. If positive, it means an upward movement.</param>
[ByRefEvent]
public struct CEZLevelBeforeMapMoveEvent(int offset, int level)
{
    /// <summary>
    /// How many levels were crossed. If negative, it means there was a downward movement. If positive, it means an upward movement.
    /// </summary>
    public int Offset = offset;

    public int CurrentZLevel = level;
}

/// <summary>
/// Is called on an entity when it moves between z-levels.
/// </summary>
/// <param name="offset">How many levels were crossed. If negative, it means there was a downward movement. If positive, it means an upward movement.</param>
public sealed class CEZLevelMapMoveEvent(int offset, int level) : EntityEventArgs
{
    /// <summary>
    /// How many levels were crossed. If negative, it means there was a downward movement. If positive, it means an upward movement.
    /// </summary>
    public int Offset = offset;

    public int CurrentZLevel = level;
}

/// <summary>
/// Is triggered when an entity falls to the lower z-levels under the force of gravity
/// </summary>
public sealed class CEZLevelFallMapEvent : EntityEventArgs;

/// <summary>
/// It is called on an entity when it hits the floor or ceiling with force.
/// </summary>
/// <param name="impactPower">The speed at the moment of impact. Always positive</param>
public sealed class CEZLevelHitEvent(float impactPower) : EntityEventArgs
{
    public float ImpactPower = impactPower;
}

/// <summary>
/// Is called every frame to calculate the current vertical velocity of the object with CEActiveZPhysicsComponent.
/// </summary>
[ByRefEvent]
public struct CEGetZVelocityEvent(Entity<CEZPhysicsComponent> target)
{
    public Entity<CEZPhysicsComponent> Target = target;
    public float VelocityDelta = 0;
}
