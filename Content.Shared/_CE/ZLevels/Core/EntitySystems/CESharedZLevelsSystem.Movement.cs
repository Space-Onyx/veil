/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Content.Shared._CE.ZLevels.Core.Components;
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

namespace Content.Shared._CE.ZLevels.Core.EntitySystems;

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

    // <Onyx-Tweak>
    private uint _groundCacheGeneration;
    // </Onyx-Tweak>

    private void InitMovement()
    {
        _highgroundQuery = GetEntityQuery<CEZLevelHighGroundComponent>();

        SubscribeLocalEvent<CEZPhysicsComponent, CEGetZVelocityEvent>(OnGetVelocity);
        SubscribeLocalEvent<CEZPhysicsComponent, CEZLevelMapMoveEvent>(OnZPhysicsMove);
        SubscribeLocalEvent<CEZPhysicsComponent, MoveEvent>(OnMoveEvent);

        SubscribeLocalEvent<DamageableComponent, CEZLevelHitEvent>(OnFallDamage);
        SubscribeLocalEvent<PhysicsComponent, CEZLevelHitEvent>(OnFallAreaImpact);

        // <Onyx-Tweak>
        SubscribeLocalEvent<CEZLevelHighGroundComponent, AnchorStateChangedEvent>(OnHighGroundAnchorChanged);
        SubscribeLocalEvent<CEZPhysicsComponent, IsWeightlessEvent>(OnZPhysicsWeightless);
        // </Onyx-Tweak>
    }

    // <Onyx-Tweak>
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
        _groundCacheGeneration++;
        var xform = Transform(ent);
        if (xform.GridUid is { } gridUid && _gridQuery.TryComp(gridUid, out var grid))
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

        var worldPos = _map.GridTileToWorldPos(gridUid, grid, tilePos);
        var box = new Box2(worldPos, worldPos + new Vector2(1f, 1f));

        foreach (var uid in _lookup.GetEntitiesIntersecting(Transform(gridUid).MapID, box, LookupFlags.Dynamic | LookupFlags.Sundries))
        {
            if (ZPhyzQuery.HasComp(uid) && !HasComp<CEActiveZPhysicsComponent>(uid))
                EnsureComp<CEActiveZPhysicsComponent>(uid);
        }
    }
    // </Onyx-Tweak>

    private void CacheMovement(Entity<CEZPhysicsComponent> ent)
    {
        if (ent.Comp.IgnoreHighGround)
            return;

        ent.Comp.CurrentGroundHeight = ComputeGroundHeightInternal((ent, ent), out var sticky);
        ent.Comp.CurrentStickyGround = sticky;
        ent.Comp.GroundCacheValid = true;
        ent.Comp.GroundCacheGeneration = _groundCacheGeneration; // <Onyx-Tweak>
    }

    private void OnMoveEvent(Entity<CEZPhysicsComponent> ent, ref MoveEvent args)
    {
        // <Onyx-Tweak>
        var groundH = ent.Comp.CurrentGroundHeight;
        var isOnHighGround = Math.Abs(groundH) > 0.001f && Math.Abs(groundH - (-1f)) > 0.001f;

        if (!isOnHighGround)
        {
            var xform = Transform(ent);
            var gridUid = xform.GridUid ?? EntityUid.Invalid;
            var tilePos = Vector2i.Zero;

            if (gridUid != EntityUid.Invalid && _gridQuery.TryComp(gridUid, out var grid))
            {
                var worldPos = _transform.GetWorldPosition(xform);
                tilePos = _map.CoordinatesToTile(gridUid, grid, new MapCoordinates(worldPos, xform.MapID));
            }

            if (gridUid == ent.Comp.CachedGridUid && tilePos == ent.Comp.CachedTilePos)
                return;

            ent.Comp.CachedGridUid = gridUid;
            ent.Comp.CachedTilePos = tilePos;
        }
        // </Onyx-Tweak>

        var oldGround = ent.Comp.CurrentGroundHeight;
        CacheMovement(ent);

        // <Onyx-Tweak>
        if (_timing.IsFirstTimePredicted && Math.Abs(ent.Comp.CurrentGroundHeight - oldGround) > 0.01f)
            EnsureComp<CEActiveZPhysicsComponent>(ent);
        // </Onyx-Tweak>
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

    private void UpdateMovement(float frameTime) // <Onyx-Tweak Edited>
    {

        _queuedLandings.Clear();    // ECHO-Tweak

        // <Onyx-Tweak Edited>
        var query = EntityQueryEnumerator<CEActiveZPhysicsComponent, CEZPhysicsComponent, TransformComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out _, out var zPhys, out var xform, out var physics))
        // </Onyx-Tweak Edited>
        {
            // ECHO-Tweak: Removed old logic
            UpdateMovement(uid, zPhys, xform, physics, frameTime);
        }

        // <Onyx-Tweak>
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
        // </Onyx-Tweak>
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

        if (!_mapQuery.TryComp(mapAboveUid.Value, out var mapComp)) // <Onyx-Tweak Edited>
            return false;

        // <Onyx-Tweak>
        var worldPos = _transform.GetWorldPosition(ent);
        foreach (var grid in GetCachedGrids(mapComp.MapId)) // <Onyx-Tweak Edited>
        {
            if (!_map.TryGetTileRef(grid.Owner, grid.Comp, worldPos, out var tileRef) ||
                tileRef.Tile.IsEmpty)
                continue;

            var tileDef = (ContentTileDefinition) TilDefMan[tileRef.Tile.TypeId];
            if (tileDef.HasZRoof)
                return true;
        }
        // </Onyx-Tweak>

        return false;
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

        if (!_mapQuery.TryComp(mapAboveUid.Value, out var mapComp)) // <Onyx-Tweak Edited>
            return false;

        // <Onyx-Tweak>
        foreach (var grid in GetCachedGrids(mapComp.MapId))
        {
            if (!_map.TryGetTileRef(grid.Owner, grid.Comp, indices, out var tileRef) ||
                tileRef.Tile.IsEmpty)
                continue;

            var tileDef = (ContentTileDefinition) TilDefMan[tileRef.Tile.TypeId];
            if (tileDef.HasZRoof)
                return true;
        }
        // </Onyx-Tweak>

        return false;
    }

    [PublicAPI]
    public void SetZPosition(Entity<CEZPhysicsComponent?> ent, float newPosition)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        ent.Comp.LocalPosition = newPosition;
        DirtyField(ent, ent.Comp, nameof(CEZPhysicsComponent.LocalPosition));
        // <Onyx-Tweak>
        if (!_timing.ApplyingState)
            EnsureComp<CEActiveZPhysicsComponent>(ent);
        // </Onyx-Tweak>
    }

    [PublicAPI]
    public void SetZGravity(Entity<CEZPhysicsComponent?> ent, float newGravityMultiplier)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        ent.Comp.GravityMultiplier = newGravityMultiplier;
        DirtyField(ent, ent.Comp, nameof(CEZPhysicsComponent.GravityMultiplier));
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
        // <Onyx-Tweak>
        EnsureComp<CEActiveZPhysicsComponent>(ent);
        // </Onyx-Tweak>
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
        // <Onyx-Tweak>
        EnsureComp<CEActiveZPhysicsComponent>(ent);
        // </Onyx-Tweak>
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

        // <Onyx-Tweak>
        if (IsOpenSpaceAtCurrentLevel(ent))
            return false;
        // <Onyx-Tweak>

        //welp, that default Chasm behavior. Not really good, but ok for now.
        if (HasComp<ChasmFallingComponent>(ent))
            return false; //Already falling

        var audio = new SoundPathSpecifier("/Audio/Effects/falling.ogg");
        _audio.PlayPredicted(audio, Transform(ent).Coordinates, ent);
        var falling = AddComp<ChasmFallingComponent>(ent);
        falling.NextDeletionTime = _timing.CurTime + falling.DeletionTime;
        _blocker.UpdateCanMove(ent);

        return false;
    }

    // <Onyx-Tweak>
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
        foreach (var grid in GetCachedGrids(mapComp.MapId)) // <Onyx-Tweak Edited>
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
    // <Onyx-Tweak>
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
// <Onyx-Tweak>
[ByRefEvent]
public struct CEGetZVelocityEvent(Entity<CEZPhysicsComponent> target)
{
    public Entity<CEZPhysicsComponent> Target = target;
    public float VelocityDelta = 0;
}
// </Onyx-Tweak>