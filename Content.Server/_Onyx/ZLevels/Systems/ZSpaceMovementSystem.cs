using Content.Server.Actions;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._Onyx.ZLevels;
using Content.Shared._Onyx.ZLevels.Components;
using Content.Shared.DoAfter;

namespace Content.Server._Onyx.ZLevels.Systems;

public sealed class ZSpaceMovementSystem : EntitySystem
{
    [Dependency] private readonly CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly ActionsSystem _actions = default!;

    private const float MoveDuration = 1f;
    private const float CheckInterval = 0.5f;

    private float _accumulator;
    private bool _hasZNetwork;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZSpaceMoverComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<ZSpaceMoverComponent, ZSpaceMoveUpAction>(OnMoveUp);
        SubscribeLocalEvent<ZSpaceMoverComponent, ZSpaceMoveDownAction>(OnMoveDown);
        SubscribeLocalEvent<ZSpaceMoverComponent, ZSpaceMoveUpDoAfterEvent>(OnMoveUpComplete);
        SubscribeLocalEvent<ZSpaceMoverComponent, ZSpaceMoveDownDoAfterEvent>(OnMoveDownComplete);
        SubscribeLocalEvent<CEZLevelsNetworkComponent, ComponentInit>(OnNetworkInit);
    }

    private void OnNetworkInit(EntityUid uid, CEZLevelsNetworkComponent comp, ComponentInit args)
    {
        _hasZNetwork = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_hasZNetwork)
            return;

        _accumulator += frameTime;
        if (_accumulator < CheckInterval)
            return;
        _accumulator -= CheckInterval;

        var query = EntityQueryEnumerator<CEZPhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            var inSpace = xform.GridUid == null && xform.MapUid != null;
            var hasMover = TryComp<ZSpaceMoverComponent>(uid, out var mover);

            if (inSpace && !hasMover)
                AddMover(uid);
            else if (!inSpace && hasMover)
                RemoveMover(uid, mover!);
        }
    }

    private void AddMover(EntityUid uid)
    {
        var mover = AddComp<ZSpaceMoverComponent>(uid);
        _actions.AddAction(uid, ref mover.UpActionEntity, mover.UpActionProto);
        _actions.AddAction(uid, ref mover.DownActionEntity, mover.DownActionProto);
    }

    private void RemoveMover(EntityUid uid, ZSpaceMoverComponent mover)
    {
        _actions.RemoveAction(mover.UpActionEntity);
        _actions.RemoveAction(mover.DownActionEntity);
        RemComp<ZSpaceMoverComponent>(uid);
    }

    private void OnRemove(EntityUid uid, ZSpaceMoverComponent comp, ComponentRemove args)
    {
        _actions.RemoveAction(comp.UpActionEntity);
        _actions.RemoveAction(comp.DownActionEntity);
    }

    private void OnMoveUp(EntityUid uid, ZSpaceMoverComponent comp, ZSpaceMoveUpAction args)
    {
        if (args.Handled)
            return;
        args.Handled = TryStartMove(uid, 1);
    }

    private void OnMoveDown(EntityUid uid, ZSpaceMoverComponent comp, ZSpaceMoveDownAction args)
    {
        if (args.Handled)
            return;
        args.Handled = TryStartMove(uid, -1);
    }

    private bool TryStartMove(EntityUid uid, int direction)
    {
        var xform = Transform(uid);
        if (xform.GridUid != null || xform.MapUid is not { } mapUid)
            return false;

        if (!_zLevels.TryMapOffset(mapUid, direction, out _))
            return false;

        var doAfterEvent = direction > 0
            ? (SimpleDoAfterEvent) new ZSpaceMoveUpDoAfterEvent()
            : new ZSpaceMoveDownDoAfterEvent();

        var args = new DoAfterArgs(EntityManager, uid, MoveDuration, doAfterEvent, uid)
        {
            BreakOnMove = false,
            BreakOnDamage = true,
            NeedHand = false,
            BlockDuplicate = true,
            CancelDuplicate = true,
        };

        return _doAfter.TryStartDoAfter(args);
    }

    private void OnMoveUpComplete(EntityUid uid, ZSpaceMoverComponent comp, ZSpaceMoveUpDoAfterEvent args)
    {
        if (!args.Cancelled && !args.Handled)
            args.Handled = DoMove(uid, 1);
    }

    private void OnMoveDownComplete(EntityUid uid, ZSpaceMoverComponent comp, ZSpaceMoveDownDoAfterEvent args)
    {
        if (!args.Cancelled && !args.Handled)
            args.Handled = DoMove(uid, -1);
    }

    private bool DoMove(EntityUid uid, int direction)
    {
        var xform = Transform(uid);
        if (xform.GridUid != null)
            return false;

        if (!_zLevels.TryMove(uid, direction))
            return false;

        // Prevent immediate fall-back after transition
        if (TryComp<CEZPhysicsComponent>(uid, out var zPhys))
        {
            _zLevels.SetZVelocity((uid, zPhys), 0);
            _zLevels.SetZPosition((uid, zPhys), 0.5f);
        }

        return true;
    }
}
