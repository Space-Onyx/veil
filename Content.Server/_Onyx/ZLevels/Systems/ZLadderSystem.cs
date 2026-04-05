using System.Numerics;
using Content.Server.Popups;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._Onyx.ZLevels.Components;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Onyx.ZLevels.Systems;

public sealed class ZLadderSystem : EntitySystem
{
    [Dependency] private readonly CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;

    private EntityQuery<MapComponent> _mapQuery;

    private const float ClimbDuration = 2f;

    public override void Initialize()
    {
        base.Initialize();
        _mapQuery = GetEntityQuery<MapComponent>();
        SubscribeLocalEvent<ZLadderComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<ZLadderComponent, ZLadderDoAfterEvent>(OnDoAfter);
    }

    private void OnInteractHand(EntityUid uid, ZLadderComponent component, InteractHandEvent args)
    {
        if (args.Handled)
            return;

        var userXform = Transform(args.User);
        if (userXform.MapUid is not { } mapUid)
            return;

        var offset = component.Direction == ZLadderDirection.Up ? 1 : -1;

        if (!_zLevels.TryMapOffset(mapUid, offset, out var targetMap))
        {
            _popup.PopupEntity(Loc.GetString("z-ladder-no-level"), args.User, args.User);
            return;
        }

        if (!_mapQuery.TryComp(targetMap, out var targetMapComp))
            return;

        var ladderWorldPos = _transform.GetWorldPosition(uid);
        var oppositeDir = component.Direction == ZLadderDirection.Up
            ? ZLadderDirection.Down
            : ZLadderDirection.Up;

        if (!TryFindMatchingLadder(targetMapComp.MapId, ladderWorldPos, oppositeDir, out _))
        {
            _popup.PopupEntity(Loc.GetString("z-ladder-no-pair"), args.User, args.User);
            return;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, args.User, ClimbDuration, new ZLadderDoAfterEvent(), uid, used: uid)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
        args.Handled = true;
    }

    private void OnDoAfter(EntityUid uid, ZLadderComponent component, ZLadderDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var userXform = Transform(args.User);
        if (userXform.MapUid is not { } mapUid)
            return;

        var offset = component.Direction == ZLadderDirection.Up ? 1 : -1;

        if (!_zLevels.TryMapOffset(mapUid, offset, out var targetMap))
            return;

        if (!_mapQuery.TryComp(targetMap, out var targetMapComp))
            return;

        var ladderWorldPos = _transform.GetWorldPosition(uid);
        var oppositeDir = component.Direction == ZLadderDirection.Up
            ? ZLadderDirection.Down
            : ZLadderDirection.Up;

        if (!TryFindMatchingLadder(targetMapComp.MapId, ladderWorldPos, oppositeDir, out var targetPos))
            return;

        _transform.SetMapCoordinates(args.User, new MapCoordinates(targetPos, targetMapComp.MapId));

        if (TryComp<PhysicsComponent>(args.User, out var phys))
            _physics.SetLinearVelocity(args.User, Vector2.Zero, body: phys);

        args.Handled = true;
    }

    private bool TryFindMatchingLadder(MapId targetMapId, Vector2 sourceWorldPos, ZLadderDirection expectedDir, out Vector2 position)
    {
        position = default;
        var bestDist = 0.5f * 0.5f;
        var found = false;

        var query = EntityQueryEnumerator<ZLadderComponent, TransformComponent>();
        while (query.MoveNext(out _, out var ladder, out var xform))
        {
            if (ladder.Direction != expectedDir)
                continue;

            if (xform.MapID != targetMapId)
                continue;

            var worldPos = _transform.GetWorldPosition(xform);
            var dist = (worldPos - sourceWorldPos).LengthSquared();
            if (dist < bestDist)
            {
                bestDist = dist;
                position = worldPos;
                found = true;
            }
        }

        return found;
    }
}
