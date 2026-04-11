using System.Numerics;
using Content.Shared._Onyx.ZLevels.Core.Components;
using Content.Shared._Onyx.ZLevels;
using Content.Shared.CCVar;
using Content.Shared.Chasm;
using Content.Shared.Gravity;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Content.Shared._Utopia.ZLevels.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;

namespace Content.Shared._Onyx.ZLevels.Core.EntitySystems;

public abstract partial class CESharedZLevelsSystem
{
    private void UpdateMovement(EntityUid uid,
                                CEZPhysicsComponent zPhys,
                                TransformComponent xform,
                                PhysicsComponent physics,
                                float frameTime)
    {
        // <Onyx-ZLevels>
        if (xform.ParentUid != xform.MapUid && !_gridQuery.HasComp(xform.ParentUid))
        {
            RemComp<CEActiveZPhysicsComponent>(uid);
            return;
        }
        if (!IsGroundCacheValidForCurrentMap(zPhys, xform))
        {
            CacheMovement((uid, zPhys));
        }
        if (!_timing.ApplyingState && Math.Abs(zPhys.Velocity) < 0.001f && Math.Abs(zPhys.LocalPosition) < 0.05f)
        {
            if (zPhys.IsGrounded && Math.Abs(zPhys.CurrentGroundHeight) < 0.001f)
            {
                RemComp<CEActiveZPhysicsComponent>(uid);
                return;
            }
            if (!zPhys.IsGrounded && zPhys.CurrentGroundHeight < -0.5f && !HasZNetworkGravity(xform) && zPhys.GravityMultiplier == 0f)
            {
                RemComp<CEActiveZPhysicsComponent>(uid);
                return;
            }
        }
        // </Onyx-ZLevels>

        var oldVelocity = zPhys.Velocity;
        var oldHeight = zPhys.LocalPosition;

        // <Onyx-ZLevels>
        if (physics.BodyStatus == BodyStatus.OnGround || HasZNetworkGravity(xform))
        {
            var velocityEv = new CEGetZVelocityEvent((uid, zPhys));
            RaiseLocalEvent(uid, ref velocityEv);

            zPhys.Velocity += velocityEv.VelocityDelta * frameTime;
        }
        // </Onyx-ZLevels>

        //Movement application
        zPhys.LocalPosition += zPhys.Velocity * frameTime;
        zPhys.Velocity = Math.Clamp(zPhys.Velocity, -ZVelocityLimit, ZVelocityLimit);

        var preGroundVelocity = zPhys.Velocity; // <Onyx-ZLevels>
        UpdateGrounded(uid, zPhys, out var landed);
        HandleLevelChange(uid, zPhys);

        if (landed) //Just landed
            HandleFalling(uid, zPhys, preGroundVelocity);

        if (Math.Abs(oldVelocity - zPhys.Velocity) > 0.01f)
            DirtyField(uid, zPhys, nameof(CEZPhysicsComponent.Velocity));

        if (Math.Abs(oldHeight - zPhys.LocalPosition) > 0.01f)
            DirtyField(uid, zPhys, nameof(CEZPhysicsComponent.LocalPosition));
    }

    private void UpdateGrounded(EntityUid uid, CEZPhysicsComponent zPhys, out bool landed)
    {
        landed = false;

        var distanceToGround = zPhys.LocalPosition - zPhys.CurrentGroundHeight;
        // <Onyx-ZLevels>
        var currentlyGrounded = distanceToGround <= MaxStepHeight || zPhys.CurrentStickyGround;
        var wasGrounded = zPhys.IsGrounded;

        if (currentlyGrounded)
        {
            zPhys.LocalPosition -= distanceToGround; //Sticky move

            // <Onyx-ZLevels>
            if (zPhys.Velocity < 0)
                zPhys.Velocity = 0;
            // </Onyx-ZLevels>
        }

        if (currentlyGrounded == wasGrounded)
            return;

        landed = !wasGrounded && currentlyGrounded;

        zPhys.IsGrounded = currentlyGrounded;
        DirtyField(uid, zPhys, nameof(CEZPhysicsComponent.IsGrounded));
    }

    // <Onyx-ZLevels>
    private void HandleFalling(EntityUid uid, CEZPhysicsComponent zPhys, float impactVelocity)
    {
        var limit = Cfg.GetCVar(CCVars.ZImpactVelocityLimit);
        if (MathF.Abs(impactVelocity) >= limit)
        {
            _queuedLandings.Add((uid, MathF.Abs(impactVelocity)));
        }

        zPhys.Velocity = -impactVelocity * zPhys.Bounciness;
    }

    private void HandleLevelChange(EntityUid uid, CEZPhysicsComponent zPhys)
    {
        if (zPhys.LocalPosition < 0) //Need teleport to ZLevel down
        {
            // <Onyx-ZLevels>
            if (_net.IsServer)
            {
                var xform = Transform(uid);
                if (xform.MapUid is { } mapUid &&
                    _zMapQuery.TryComp(mapUid, out var zMap))
                {
                    if (_timing.CurTime < zMap.SuppressFallsUntil)
                    {
                        zPhys.LocalPosition = 0;
                        if (zPhys.Velocity < 0)
                            zPhys.Velocity = 0;
                        return;
                    }

                    if (zMap.SuppressFallsUntil != TimeSpan.Zero)
                        zMap.SuppressFallsUntil = TimeSpan.Zero;
                }
            }
            // </Onyx-ZLevels>

            // <Onyx-ZLevels> Block Z-level fall when no gravity (open space)
            if (!HasZNetworkGravity(Transform(uid)) && zPhys.GravityMultiplier == 0f)
            {
                zPhys.LocalPosition = 0;
                if (zPhys.Velocity < 0)
                    zPhys.Velocity = 0;
                return;
            }

            if (!TryMoveDownOrChasm(uid))
            {
                if (!HasComp<ChasmFallingComponent>(uid))
                {
                    zPhys.LocalPosition = 0;
                    if (zPhys.Velocity < 0)
                        zPhys.Velocity = 0;
                }
                return;
            }
            // </Onyx-ZLevels>

            zPhys.LocalPosition += 1;

            // <Onyx-ZLevels>
            if (zPhys.CurrentGroundHeight > 0)
            {
                zPhys.LocalPosition = 0;
                zPhys.CurrentGroundHeight = 0;
                zPhys.IsGrounded = true;
                zPhys.Velocity = 0;
                zPhys.GroundCacheValid = true;
                if (Transform(uid).MapUid is { } currentMapUid)
                {
                    zPhys.GroundCacheMapUid = currentMapUid;
                    zPhys.GroundCacheGeneration = GetGroundCacheGenerationForMap(currentMapUid);
                }
                DirtyField(uid, zPhys, nameof(CEZPhysicsComponent.LocalPosition));
                DirtyField(uid, zPhys, nameof(CEZPhysicsComponent.Velocity));
                DirtyField(uid, zPhys, nameof(CEZPhysicsComponent.IsGrounded));
                return;
            }
            zPhys.GroundCacheValid = false;
            // </Onyx-ZLevels>

            if (zPhys.CurrentStickyGround)
                return;

            var fallEv = new CEZLevelFallMapEvent();
            RaiseLocalEvent(uid, fallEv);
        }

        else if (zPhys.LocalPosition >= 1) //Need teleport to ZLevel up
        {
            var hasTile = HasTileAbove(uid);

            if (hasTile) //Hit roof
            {
                if (MathF.Abs(zPhys.Velocity) >= Cfg.GetCVar(CCVars.ZImpactVelocityLimit)) // <Onyx-ZLevels>
                {
                    _queuedLandings.Add((uid, MathF.Abs(zPhys.Velocity))); // <Onyx-Tweak Edited>
                }

                zPhys.LocalPosition = 1;
                zPhys.Velocity = -zPhys.Velocity * zPhys.Bounciness;
            }
            else //Move up
            {
                if (TryMoveUp(uid))
                {
                    zPhys.LocalPosition -= 1;

                    // <Onyx-ZLevels>
                    zPhys.LocalPosition = 0;
                    zPhys.CurrentGroundHeight = 0;
                    zPhys.IsGrounded = true;
                    zPhys.Velocity = 0;
                    zPhys.CurrentStickyGround = true;
                    zPhys.GroundCacheValid = false;
                    DirtyField(uid, zPhys, nameof(CEZPhysicsComponent.LocalPosition));
                    DirtyField(uid, zPhys, nameof(CEZPhysicsComponent.Velocity));
                    DirtyField(uid, zPhys, nameof(CEZPhysicsComponent.IsGrounded));
                    // </Onyx-ZLevels>
                }
            }
        }
    }

    /// <summary>
    /// Computes the "ground height" relative to the entity's current Z-level baseline.
    /// Returns values where 0 means ground on the same level, -1 means ground one level below,
    /// and intermediate values are possible for high ground entities (stairs).
    /// </summary>
    private float ComputeGroundHeightInternal(Entity<CEZPhysicsComponent?> target, out bool stickyGround, int maxFloors = 1)
    {
        stickyGround = false;
        if (!Resolve(target, ref target.Comp, false))
            return 0;

        var xform = Transform(target);
        if (!_zMapQuery.TryComp(xform.MapUid, out var zMapComp))
            return 0;

        var worldPos = _transform.GetWorldPosition(target);

        // Fast and robust fallback: if entity is currently on a grid and the local tile is solid,
        // treat the current floor as ground even if per-map grid caches are temporarily stale.
        if (xform.GridUid is { } currentGridUid && _gridQuery.TryComp(currentGridUid, out var currentGrid))
        {
            var currentMapCoords = new MapCoordinates(worldPos, xform.MapID);
            var currentTilePos = _map.CoordinatesToTile(currentGridUid, currentGrid, currentMapCoords);

            var currentTileEnts = _map.GetAnchoredEntitiesEnumerator(currentGridUid, currentGrid, currentTilePos);
            while (currentTileEnts.MoveNext(out var ent))
            {
                if (!_highgroundQuery.TryComp(ent, out var heightComp))
                    continue;

                var uid = ent.Value;
                var fix = _fix.GetFixtureOrNull(uid, heightComp.FixtureId);
                if (fix == null || fix.Shape is not PolygonShape shape)
                    continue;

                var aabb = shape.ComputeAABB(new Transform(0f), 0);
                var bottom = aabb.Bottom;
                var top = aabb.Top;
                var length = Math.Abs(top - bottom);

                var (pos, rot) = _transform.GetWorldPositionRotation(uid);
                var bottomPos = rot.RotateVec(new Vector2(0, bottom)) + pos;

                var curve = heightComp.HeightCurve;
                if (curve.Count == 0)
                    continue;

                if (curve.Count == 1)
                    return curve[0];

                var worldDir = rot.RotateVec(new Vector2(0, length));
                var lengthWorld = worldDir.Length();
                if (lengthWorld == 0)
                    continue;

                stickyGround = heightComp.Stick;

                var relPos = worldPos - bottomPos;
                var tRaw = Vector2.Dot(relPos, worldDir) / (lengthWorld * lengthWorld);
                var t = Math.Clamp(tRaw, 0f, 1f);
                t = 1f - t;

                float index = t * (curve.Count - 1);
                int lower = (int)Math.Floor(index);
                int upper = Math.Min(lower + 1, curve.Count - 1);
                float frac = index - lower;
                return curve[lower] * (1 - frac) + curve[upper] * frac;
            }

            if (_map.TryGetTileRef(currentGridUid, currentGrid, currentTilePos, out var currentTileRef) &&
                !currentTileRef.Tile.IsEmpty)
            {
                return 0f;
            }
        }

        // </Onyx-ZLevels>

        //Select current map by default
        Entity<CEZLevelMapComponent> checkingMap = (xform.MapUid.Value, zMapComp);

        for (var floor = 0; floor <= maxFloors; floor++)
        {
            if (floor != 0) //Select map below
            {
                if (!TryMapOffset((checkingMap.Owner, checkingMap.Comp), -floor, out var tempCheckingMap))
                    continue;

                checkingMap = tempCheckingMap.Value;
            }

            // <Onyx-Tweak Edited>
            if (!_mapQuery.TryComp(checkingMap, out var checkingMapComp))
                continue;

            var mapCoords = new MapCoordinates(worldPos, checkingMapComp.MapId);

            // Check all grids on this map (map grid + linked grids)
            foreach (var grid in GetCachedGrids(checkingMapComp.MapId)) // <Onyx-Tweak Edited>
            {
                var tilePos = _map.CoordinatesToTile(grid.Owner, grid.Comp, mapCoords);

                //Check all types of ZHeight entities
                var query = _map.GetAnchoredEntitiesEnumerator(grid.Owner, grid.Comp, tilePos);
                while (query.MoveNext(out var ent))
                {
                    if (!_highgroundQuery.TryComp(ent, out var heightComp))
                        continue;

                    var uid = ent.Value;

                    var fix = _fix.GetFixtureOrNull(uid, heightComp.FixtureId);

                    if (fix == null || fix.Shape is not PolygonShape shape)
                        continue;

                    var aabb = shape.ComputeAABB(new Transform(0f), 0);
                    var bottom = aabb.Bottom;
                    var top = aabb.Top;
                    var length = Math.Abs(top - bottom);

                    var (pos, rot) = _transform.GetWorldPositionRotation(uid);

                    var bottomPos = rot.RotateVec(new Vector2(0, bottom)) + pos;

                    var curve = heightComp.HeightCurve;
                    if (curve.Count == 0)
                        continue;

                    if (curve.Count == 1)
                    {
                        var groundY = -floor + curve[0];
                        return groundY;
                    }

                    var worldDir = rot.RotateVec(new Vector2(0, length));
                    var lengthWorld = worldDir.Length();
                    if (lengthWorld == 0)
                        continue;

                    stickyGround = heightComp.Stick;

                    var relPos = worldPos - bottomPos;
                    var tRaw = Vector2.Dot(relPos, worldDir) / (lengthWorld * lengthWorld);
                    var t = Math.Clamp(tRaw, 0f, 1f);
                    t = 1f - t;

                    float index = t * (curve.Count - 1);
                    int lower = (int)Math.Floor(index);
                    int upper = Math.Min(lower + 1, curve.Count - 1);
                    float frac = index - lower;
                    var y = curve[lower] * (1 - frac) + curve[upper] * frac;

                    return -floor + y;
                }

                //No ZEntities found on this grid, check floor tiles
                if (_map.TryGetTileRef(grid.Owner, grid.Comp, tilePos, out var tileRef) &&
                    !tileRef.Tile.IsEmpty)
                {
                    if (floor > 0)
                    {
                        var tileDef = (ContentTileDefinition) TilDefMan[tileRef.Tile.TypeId];
                        if (TileZRoof.HasZRoof(grid, tileRef.GridIndices, tileDef.HasZRoof) &&
                            !IsOverInteriorHole(checkingMap, worldPos))
                            return -(floor - 1);
                    }

                    return -floor;
                }
            }
            // </Onyx-Tweak Edited>
        }

        return -maxFloors;
    }

    // <Onyx-ZLevels>
    private readonly Dictionary<(EntityUid LowerGrid, EntityUid UpperGrid), UpperGridCoverageCacheEntry> _upperGridCoverageCache = new();
    private readonly Dictionary<EntityUid, UpperGridInteriorHoleCacheEntry> _upperGridInteriorHolesCache = new();
    private bool IsOverInteriorHole(EntityUid lowerMapUid, Vector2 worldPos)
    {
        if (!_zMapQuery.HasComp(lowerMapUid) || !_mapQuery.TryComp(lowerMapUid, out var lowerMapComp))
            return false;

        if (!TryMapUp(lowerMapUid, out var upperMap) ||
            !_mapQuery.TryComp(upperMap.Value.Owner, out var upperMapComp))
            return false;

        var lowerGrids = GetCachedGrids(lowerMapComp.MapId);
        var upperGrids = GetCachedGrids(upperMapComp.MapId);

        foreach (var lowerGrid in lowerGrids)
        {
            if (!_motionLinkQuery.TryComp(lowerGrid.Owner, out var lowerLink) || string.IsNullOrEmpty(lowerLink.GroupId))
                continue;

            var lowerTilePos = _map.WorldToTile(lowerGrid.Owner, lowerGrid.Comp, worldPos);

            foreach (var upperGrid in upperGrids)
            {
                if (!_motionLinkQuery.TryComp(upperGrid.Owner, out var upperLink))
                    continue;

                if (upperLink.GroupId != lowerLink.GroupId)
                    continue;

                var holes = GetUpperGridInteriorHoles(lowerGrid, upperGrid);
                if (holes.Contains(lowerTilePos))
                    return true;
            }
        }

        return false;
    }

    private HashSet<Vector2i> GetUpperGridInteriorHoles(Entity<MapGridComponent> lowerGrid, Entity<MapGridComponent> upperGrid)
    {
        var lowerGridMapUid = Transform(lowerGrid.Owner).MapUid;
        var upperGridMapUid = Transform(upperGrid.Owner).MapUid;
        if (lowerGridMapUid is null || upperGridMapUid is null)
            return new HashSet<Vector2i>();

        var lowerMapGeneration = GetGroundCacheGenerationForMap(lowerGridMapUid.Value);
        var upperMapGeneration = GetGroundCacheGenerationForMap(upperGridMapUid.Value);

        var key = (lowerGrid.Owner, upperGrid.Owner);
        if (_upperGridCoverageCache.TryGetValue(key, out var cached) &&
            cached.LowerMapGeneration == lowerMapGeneration &&
            cached.UpperMapGeneration == upperMapGeneration)
        {
            return cached.Coverage;
        }

        HashSet<Vector2i> upperInteriorHoles;
        if (_upperGridInteriorHolesCache.TryGetValue(upperGrid.Owner, out var upperCached) &&
            upperCached.UpperMapGeneration == upperMapGeneration)
        {
            upperInteriorHoles = upperCached.Holes;
        }
        else
        {
            upperInteriorHoles = ZLevelFloodFillHelper.FindInteriorHoles(_map, upperGrid, TilDefMan);
            _upperGridInteriorHolesCache[upperGrid.Owner] = new UpperGridInteriorHoleCacheEntry(
                upperMapGeneration,
                upperInteriorHoles);
        }

        var projectedCoverage = new HashSet<Vector2i>(upperInteriorHoles.Count);
        foreach (var upperTilePos in upperInteriorHoles)
        {
            var worldPos = _map.GridTileToWorldPos(upperGrid.Owner, upperGrid.Comp, upperTilePos);
            var lowerTilePos = _map.WorldToTile(lowerGrid.Owner, lowerGrid.Comp, worldPos);
            projectedCoverage.Add(lowerTilePos);
        }

        _upperGridCoverageCache[key] = new UpperGridCoverageCacheEntry(
            lowerMapGeneration,
            upperMapGeneration,
            projectedCoverage);

        return projectedCoverage;
    }

    public bool HasGravityOnMap(MapId mapId)
    {
        if (_mapGravityCache.TryGetValue(mapId, out var cachedGravity))
            return cachedGravity;

        foreach (var grid in GetCachedGrids(mapId))
        {
            if (_gravityQuery.TryComp(grid.Owner, out var gravity) && gravity.Enabled)
            {
                _mapGravityCache[mapId] = true;
                return true;
            }
        }
        if (HasMapEntityGravity(mapId))
        {
            _mapGravityCache[mapId] = true;
            return true;
        }

        _mapGravityCache[mapId] = false;
        return false;
    }
    public bool HasMapEntityGravity(MapId mapId)
    {
        var mapEntityId = _mapManager.GetMapEntityId(mapId);
        return mapEntityId.IsValid() && _gravityQuery.TryComp(mapEntityId, out var mapGravity) && mapGravity.Enabled;
    }

    public bool IsEntityOverInteriorHole(TransformComponent xform)
    {
        if (xform.GridUid != null)
            return false;

        if (xform.MapUid is not { } mapUid ||
            !_zMapQuery.TryComp(mapUid, out var zMap))
            return false;

        var worldPos = _transform.GetWorldPosition(xform);

        if (IsOverInteriorHole(mapUid, worldPos))
            return true;

        if (TryMapDown((mapUid, zMap), out var belowMap) &&
            IsOverInteriorHole(belowMap.Value.Owner, worldPos))
            return true;

        return false;
    }

    public bool HasZNetworkGravity(TransformComponent xform)
    {
        if (xform.MapUid is not { } mapUid ||
            !_zMapQuery.TryComp(mapUid, out var zMap) ||
            !_mapQuery.TryComp(mapUid, out var mapComp))
            return false;

        if (xform.GridUid is { } gridUid)
            return _gravityQuery.TryComp(gridUid, out var gridGravity) && gridGravity.Enabled;

        var worldPos = _transform.GetWorldPosition(xform);
        var currentMap = (mapUid, zMap);
        var inInteriorHole = IsOverInteriorHole(mapUid, worldPos);

        if (!inInteriorHole &&
            TryMapDown(currentMap, out var belowMap))
        {
            inInteriorHole = IsOverInteriorHole(belowMap.Value.Owner, worldPos);
        }

        if (!inInteriorHole)
            return false;

        if (HasGravityOnMap(mapComp.MapId))
            return true;

        if (TryMapUp(currentMap, out var mapAbove) &&
            _mapQuery.TryComp(mapAbove.Value.Owner, out var aboveMapComp) &&
            HasGravityOnMap(aboveMapComp.MapId))
            return true;

        if (TryMapDown(currentMap, out var mapBelow) &&
            _mapQuery.TryComp(mapBelow.Value.Owner, out var belowMapComp) &&
            HasGravityOnMap(belowMapComp.MapId))
            return true;

        return false;
    }
    // </Onyx-ZLevels>
}
