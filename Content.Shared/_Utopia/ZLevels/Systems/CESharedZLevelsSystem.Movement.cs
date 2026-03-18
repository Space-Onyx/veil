using System.Numerics;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.CCVar;
using Content.Shared.Chasm;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
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
            if (!zPhys.IsGrounded && zPhys.CurrentGroundHeight < -0.5f)
            {
                RemComp<CEActiveZPhysicsComponent>(uid);
                return;
            }
        }
        // </Onyx-Tweak>

        var oldVelocity = zPhys.Velocity;
        var oldHeight = zPhys.LocalPosition;

        if (physics.BodyStatus == BodyStatus.OnGround)
        {
            //Velocity application
            var velocityEv = new CEGetZVelocityEvent((uid, zPhys));
            RaiseLocalEvent(uid, ref velocityEv); // <Onyx-Tweak Edited>

            zPhys.Velocity += velocityEv.VelocityDelta * frameTime;
        }

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
                    zPhys.GroundCacheValid = false; // <Onyx-Tweak>

                    // <Onyx-Tweak>
                    zPhys.LocalPosition = 0;
                    zPhys.CurrentGroundHeight = 0;
                    zPhys.IsGrounded = true;
                    zPhys.Velocity = 0;
                    zPhys.GroundCacheValid = true;
                    zPhys.GroundCacheGeneration = _groundCacheGeneration;
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

                    // <Onyx-Tweak Edited>
                    var aabb = shape.ComputeAABB(new Transform(0f), 0);
                    var bottom = aabb.Bottom;
                    var top = aabb.Top;
                    // </Onyx-Tweak Edited>
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
                    return -floor;
            }
            // </Onyx-Tweak Edited>
        }

        return -maxFloors;
    }
}
