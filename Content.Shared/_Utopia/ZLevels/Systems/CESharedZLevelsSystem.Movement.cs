using System.Numerics;
using Content.Shared._CE.ZLevels.Core.Components;
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

namespace Content.Shared._CE.ZLevels.Core.EntitySystems;

public abstract partial class CESharedZLevelsSystem
{
    private void UpdateMovement(EntityUid uid,
                                CEZPhysicsComponent zPhys,
                                TransformComponent xform,
                                PhysicsComponent physics,
                                float frameTime)
    {
        // <Onyx-Tweak>
        if (!zPhys.GroundCacheValid || zPhys.GroundCacheGeneration != _groundCacheGeneration)
        {
            CacheMovement((uid, zPhys));
        }

        // <Onyx-Tweak>
        if (Math.Abs(zPhys.Velocity) < 0.001f && Math.Abs(zPhys.LocalPosition) < 0.05f)
        {
            if (zPhys.IsGrounded && Math.Abs(zPhys.CurrentGroundHeight) < 0.001f)
            {
                RemComp<CEActiveZPhysicsComponent>(uid);
                return;
            }
            // <Onyx-Tweak>
            if (!zPhys.IsGrounded && zPhys.CurrentGroundHeight < -0.5f && !HasZNetworkGravity(xform))
            {
                RemComp<CEActiveZPhysicsComponent>(uid);
                return;
            }
            // </Onyx-Tweak>
        }
        // </Onyx-Tweak>

        var oldVelocity = zPhys.Velocity;
        var oldHeight = zPhys.LocalPosition;

        // <Onyx-Tweak>
        if (physics.BodyStatus == BodyStatus.OnGround || HasZNetworkGravity(xform))
        {
            //Velocity application
            var velocityEv = new CEGetZVelocityEvent((uid, zPhys));
            RaiseLocalEvent(uid, ref velocityEv);

            zPhys.Velocity += velocityEv.VelocityDelta * frameTime;
        }
        // </Onyx-Tweak>

        //Movement application
        zPhys.LocalPosition += zPhys.Velocity * frameTime;
        zPhys.Velocity = Math.Clamp(zPhys.Velocity, -ZVelocityLimit, ZVelocityLimit);

        var preGroundVelocity = zPhys.Velocity; // <Onyx-Tweak>
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
        // <Onyx-Tweak>
        var currentlyGrounded = distanceToGround <= MaxStepHeight || zPhys.CurrentStickyGround;

        if (currentlyGrounded)
        {
            zPhys.LocalPosition -= distanceToGround; //Sticky move

            // <Onyx-Tweak>
            if (zPhys.Velocity < 0)
                zPhys.Velocity = 0;
            // </Onyx-Tweak>
        }

        if (currentlyGrounded == zPhys.IsGrounded)
            return;

        landed = !zPhys.IsGrounded && currentlyGrounded;

        zPhys.IsGrounded = currentlyGrounded;

        if (currentlyGrounded != zPhys.IsGrounded)
            DirtyField(uid, zPhys, nameof(CEZPhysicsComponent.IsGrounded));
    }

    // <Onyx-Tweak>
    private void HandleFalling(EntityUid uid, CEZPhysicsComponent zPhys, float impactVelocity)
    {
        var limit = Cfg.GetCVar(CCVars.ZImpactVelocityLimit);
        if (MathF.Abs(impactVelocity) >= limit)
        {
            _queuedLandings.Add((uid, -impactVelocity));
        }

        zPhys.Velocity = -impactVelocity * zPhys.Bounciness;
    }

    private void HandleLevelChange(EntityUid uid, CEZPhysicsComponent zPhys)
    {
        if (zPhys.LocalPosition < 0) //Need teleport to ZLevel down
        {
            // <Onyx-Tweak>
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
            // <Onyx-Tweak>

            zPhys.LocalPosition += 1;

            // <Onyx-Tweak>
            if (zPhys.CurrentGroundHeight > 0)
            {
                zPhys.LocalPosition = 0;
                zPhys.CurrentGroundHeight = 0;
                zPhys.IsGrounded = true;
                zPhys.Velocity = 0;
                zPhys.GroundCacheValid = true;
                zPhys.GroundCacheGeneration = _groundCacheGeneration;
                DirtyField(uid, zPhys, nameof(CEZPhysicsComponent.LocalPosition));
                DirtyField(uid, zPhys, nameof(CEZPhysicsComponent.Velocity));
                DirtyField(uid, zPhys, nameof(CEZPhysicsComponent.IsGrounded));
                return;
            }
            zPhys.GroundCacheValid = false;
            // </Onyx-Tweak>

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
                if (MathF.Abs(zPhys.Velocity) >= Cfg.GetCVar(CCVars.ZImpactVelocityLimit)) // <Onyx-Tweak>
                {
                    _queuedLandings.Add((uid, zPhys.Velocity)); // <Onyx-Tweak Edited>
                }

                zPhys.LocalPosition = 1;
                zPhys.Velocity = -zPhys.Velocity * zPhys.Bounciness;
            }
            else //Move up
            {
                if (TryMoveUp(uid))
                {
                    zPhys.LocalPosition -= 1;

                    // <Onyx-Tweak>
                    zPhys.LocalPosition = 0;
                    zPhys.CurrentGroundHeight = 0;
                    zPhys.IsGrounded = true;
                    zPhys.Velocity = 0;
                    zPhys.CurrentStickyGround = true;
                    zPhys.GroundCacheValid = false;
                    DirtyField(uid, zPhys, nameof(CEZPhysicsComponent.LocalPosition));
                    DirtyField(uid, zPhys, nameof(CEZPhysicsComponent.Velocity));
                    DirtyField(uid, zPhys, nameof(CEZPhysicsComponent.IsGrounded));
                    // </Onyx-Tweak>
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

        // </Onyx-Tweak>

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
                        if (tileDef.HasZRoof && !IsOverInteriorHole(checkingMap, worldPos))
                            return -(floor - 1);
                    }

                    return -floor;
                }
            }
            // </Onyx-Tweak Edited>
        }

        return -maxFloors;
    }

    // <Onyx-Tweak>
    private readonly Dictionary<(EntityUid, EntityUid), HashSet<Vector2i>> _upperGridCoverageCache = new();
    private uint _coverageCacheGeneration;
    private bool IsOverInteriorHole(EntityUid lowerMapUid, Vector2 worldPos)
    {
        if (!_zMapQuery.HasComp(lowerMapUid))
            return false;

        if (!_mapQuery.TryComp(lowerMapUid, out var mapComp))
            return false;

        foreach (var grid in GetCachedGrids(mapComp.MapId))
        {
            if (!_motionLinkQuery.TryComp(grid.Owner, out var link) || string.IsNullOrEmpty(link.GroupId))
                continue;

            if (!TryMapUp(lowerMapUid, out var upperMap) ||
                !_mapQuery.TryComp(upperMap.Value.Owner, out var upperMapComp))
                return false;

            foreach (var upperGrid in GetCachedGrids(upperMapComp.MapId))
            {
                if (!_motionLinkQuery.TryComp(upperGrid.Owner, out var upperLink))
                    continue;

                if (upperLink.GroupId != link.GroupId)
                    continue;

                var holes = GetUpperGridInteriorHoles(grid, upperGrid);
                var lowerTilePos = _map.WorldToTile(grid.Owner, grid.Comp, worldPos);
                if (holes.Contains(lowerTilePos))
                    return true;
            }

            break;
        }

        return false;
    }
    private HashSet<Vector2i> GetUpperGridInteriorHoles(Entity<MapGridComponent> lowerGrid, Entity<MapGridComponent> upperGrid)
    {
        if (_coverageCacheGeneration != _groundCacheGeneration)
        {
            _upperGridCoverageCache.Clear();
            _coverageCacheGeneration = _groundCacheGeneration;
        }

        var key = (lowerGrid.Owner, upperGrid.Owner);
        if (_upperGridCoverageCache.TryGetValue(key, out var cached))
            return cached;

        var interiorHoles = new HashSet<Vector2i>();
        var solidTiles = new HashSet<Vector2i>();

        var enumerator = _map.GetAllTilesEnumerator(upperGrid.Owner, upperGrid.Comp, ignoreEmpty: true);
        while (enumerator.MoveNext(out var upperTileRef))
        {
            solidTiles.Add(upperTileRef.Value.GridIndices);
        }

        if (solidTiles.Count == 0)
        {
            _upperGridCoverageCache[key] = interiorHoles;
            return interiorHoles;
        }

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        foreach (var pos in solidTiles)
        {
            if (pos.X < minX) minX = pos.X;
            if (pos.Y < minY) minY = pos.Y;
            if (pos.X > maxX) maxX = pos.X;
            if (pos.Y > maxY) maxY = pos.Y;
        }
        minX--; minY--; maxX++; maxY++;

        var outerEmpty = new HashSet<Vector2i>();
        var queue = new Queue<Vector2i>();

        void TryEnqueue(Vector2i p)
        {
            if (solidTiles.Contains(p)) return;
            if (!outerEmpty.Add(p)) return;
            queue.Enqueue(p);
        }

        for (var x = minX; x <= maxX; x++)
        {
            TryEnqueue(new Vector2i(x, minY));
            TryEnqueue(new Vector2i(x, maxY));
        }
        for (var y = minY + 1; y < maxY; y++)
        {
            TryEnqueue(new Vector2i(minX, y));
            TryEnqueue(new Vector2i(maxX, y));
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.X < minX || current.X > maxX || current.Y < minY || current.Y > maxY)
                continue;
            TryEnqueue(new Vector2i(current.X + 1, current.Y));
            TryEnqueue(new Vector2i(current.X - 1, current.Y));
            TryEnqueue(new Vector2i(current.X, current.Y + 1));
            TryEnqueue(new Vector2i(current.X, current.Y - 1));
        }

        for (var x = minX + 1; x < maxX; x++)
        {
            for (var y = minY + 1; y < maxY; y++)
            {
                var pos = new Vector2i(x, y);
                if (solidTiles.Contains(pos)) continue;
                if (outerEmpty.Contains(pos)) continue;

                var wp = _map.GridTileToWorldPos(upperGrid.Owner, upperGrid.Comp, pos);
                var lowerTilePos = _map.WorldToTile(lowerGrid.Owner, lowerGrid.Comp, wp);
                interiorHoles.Add(lowerTilePos);
            }
        }

        _upperGridCoverageCache[key] = interiorHoles;
        return interiorHoles;
    }

    private bool HasZNetworkGravity(TransformComponent xform)
    {
        if (xform.MapUid is not { } mapUid)
            return false;

        if (!_zMapQuery.HasComp(mapUid) || !_mapQuery.TryComp(mapUid, out var mapComp))
            return false;

        foreach (var grid in GetCachedGrids(mapComp.MapId))
        {
            if (_gravityQuery.TryComp(grid.Owner, out var gravity) && gravity.Enabled)
                return true;
        }

        if (TryMapUp(mapUid, out var mapAbove) &&
            _mapQuery.TryComp(mapAbove.Value.Owner, out var aboveMapComp))
        {
            foreach (var grid in GetCachedGrids(aboveMapComp.MapId))
            {
                if (_gravityQuery.TryComp(grid.Owner, out var gravAbove) && gravAbove.Enabled)
                    return true;
            }
        }

        return false;
    }
    // </Onyx-Tweak>
}
