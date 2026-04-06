using Content.Server.Actions;
using Content.Shared._Onyx.ZLevels.Core.Components;
using Content.Shared._Onyx.ZLevels.Core.EntitySystems;
using Content.Shared._Onyx.ZLevels;
using Content.Shared._Onyx.ZLevels.Components;
using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.ZLevels.Systems;

public sealed class ZSpaceMovementSystem : EntitySystem
{
    [Dependency] private readonly CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const float MoveDuration = 1f;
    private const float CheckInterval = 0.5f;
    private const float SpaceIntersectRecheckInterval = 1.5f;
    private const float CandidateRescanInterval = 10f;

    private float _accumulator;
    private float _candidateRescanAccumulator;
    private int _zNetworkCount;
    private List<Entity<MapGridComponent>> _gridBuffer = new();
    private readonly Dictionary<EntityUid, TimeSpan> _nextIntersectCheck = new();
    private readonly Dictionary<EntityUid, bool> _mapHasAdjacentLayerCache = new();
    private readonly HashSet<EntityUid> _floatingCandidates = new();
    private readonly List<EntityUid> _entityBuffer = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ZSpaceMoverComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<ZSpaceMoverComponent, ZSpaceMoveUpAction>(OnMoveUp);
        SubscribeLocalEvent<ZSpaceMoverComponent, ZSpaceMoveDownAction>(OnMoveDown);
        SubscribeLocalEvent<ZSpaceMoverComponent, ZSpaceMoveUpDoAfterEvent>(OnMoveUpComplete);
        SubscribeLocalEvent<ZSpaceMoverComponent, ZSpaceMoveDownDoAfterEvent>(OnMoveDownComplete);
        SubscribeLocalEvent<CEZPhysicsComponent, ComponentStartup>(OnZPhysicsStartup);
        SubscribeLocalEvent<CEZPhysicsComponent, ComponentShutdown>(OnZPhysicsShutdown);
        _transform.OnGlobalMoveEvent += OnGlobalMove;

        RefreshNetworkCount();

        RebuildFloatingCandidates();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _transform.OnGlobalMoveEvent -= OnGlobalMove;
    }

    private void OnZPhysicsStartup(EntityUid uid, CEZPhysicsComponent comp, ComponentStartup args)
    {
        RefreshFloatingCandidate(uid);
    }

    private void OnZPhysicsShutdown(EntityUid uid, CEZPhysicsComponent comp, ComponentShutdown args)
    {
        RemoveFloatingCandidate(uid);
    }

    private void OnGlobalMove(ref MoveEvent ev)
    {
        if (!HasComp<CEZPhysicsComponent>(ev.Sender))
            return;

        RefreshFloatingCandidate(ev.Sender);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var hadNetworks = _zNetworkCount > 0;
        RefreshNetworkCount();
        if (_zNetworkCount <= 0)
        {
            if (hadNetworks)
                ClearMoversAndCaches();
            return;
        }

        _candidateRescanAccumulator += frameTime;
        if (_candidateRescanAccumulator >= CandidateRescanInterval)
        {
            _candidateRescanAccumulator = 0f;
            RebuildFloatingCandidates();
        }

        _accumulator += frameTime;
        if (_accumulator < CheckInterval)
            return;
        _accumulator -= CheckInterval;
        _mapHasAdjacentLayerCache.Clear();

        _entityBuffer.Clear();
        _entityBuffer.AddRange(_floatingCandidates);
        foreach (var uid in _entityBuffer)
        {
            if (!TryComp<TransformComponent>(uid, out var xform))
            {
                RemoveFloatingCandidate(uid);
                continue;
            }

            var hasMover = TryComp<ZSpaceMoverComponent>(uid, out var spaceMover);

            if (xform.GridUid != null || xform.MapUid == null)
            {
                if (hasMover)
                    RemoveFloatingCandidate(uid, spaceMover!);
                else
                    RemoveFloatingCandidate(uid);
                continue;
            }

            var mapUid = xform.MapUid.Value;
            if (!HasAdjacentZLayer(mapUid))
            {
                if (hasMover)
                    RemoveFloatingCandidate(uid, spaceMover!);
                else
                    RemoveFloatingCandidate(uid);
                continue;
            }

            var worldPos = _transform.GetWorldPosition(xform);

            // Fast path: if there is a grid tile under the entity, it is definitely not in open space.
            if (_mapManager.TryFindGridAt(mapUid, worldPos, out _, out _))
            {
                _nextIntersectCheck.Remove(uid);
                if (hasMover)
                    RemoveMover(uid, spaceMover!);
                continue;
            }

            if (hasMover
                && _nextIntersectCheck.TryGetValue(uid, out var nextCheckAt)
                && _timing.CurTime < nextCheckAt)
            {
                continue;
            }

            // Entity has no GridUid and no tile underfoot; it may still overlap a grid AABB via hole tiles.
            var pointBox = new Box2(worldPos, worldPos);
            _gridBuffer.Clear();
            _mapManager.FindGridsIntersecting(mapUid, pointBox, ref _gridBuffer, approx: true);

            var inSpace = _gridBuffer.Count == 0;

            if (inSpace && !hasMover)
                AddMover(uid);

            if (inSpace)
            {
                _nextIntersectCheck[uid] = _timing.CurTime + TimeSpan.FromSeconds(SpaceIntersectRecheckInterval);
            }
            else
            {
                _nextIntersectCheck.Remove(uid);
            }

            if (!inSpace && hasMover)
                RemoveMover(uid, spaceMover!);
        }
    }

    private bool HasAdjacentZLayer(EntityUid mapUid)
    {
        if (_mapHasAdjacentLayerCache.TryGetValue(mapUid, out var hasAdjacent))
            return hasAdjacent;

        hasAdjacent = _zLevels.TryMapOffset(mapUid, 1, out _) || _zLevels.TryMapOffset(mapUid, -1, out _);
        _mapHasAdjacentLayerCache[mapUid] = hasAdjacent;
        return hasAdjacent;
    }

    private void RefreshNetworkCount()
    {
        _zNetworkCount = 0;
        var query = EntityQueryEnumerator<CEZLevelsNetworkComponent>();
        while (query.MoveNext(out _, out _))
        {
            _zNetworkCount++;
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
        _nextIntersectCheck.Remove(uid);
    }

    private void OnRemove(EntityUid uid, ZSpaceMoverComponent comp, ComponentRemove args)
    {
        _actions.RemoveAction(comp.UpActionEntity);
        _actions.RemoveAction(comp.DownActionEntity);
        _nextIntersectCheck.Remove(uid);
    }

    private void RefreshFloatingCandidate(EntityUid uid)
    {
        if (!TryComp<TransformComponent>(uid, out var xform))
        {
            RemoveFloatingCandidate(uid);
            return;
        }

        if (xform.GridUid != null || xform.MapUid == null)
        {
            TryComp<ZSpaceMoverComponent>(uid, out var mover);
            RemoveFloatingCandidate(uid, mover);
            return;
        }

        _floatingCandidates.Add(uid);
    }

    private void RemoveFloatingCandidate(EntityUid uid, ZSpaceMoverComponent? mover = null)
    {
        _floatingCandidates.Remove(uid);
        _nextIntersectCheck.Remove(uid);

        if (mover != null)
            RemoveMover(uid, mover);
    }

    private void RebuildFloatingCandidates()
    {
        var query = EntityQueryEnumerator<CEZPhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == null && xform.MapUid != null)
                _floatingCandidates.Add(uid);
        }
    }

    private void ClearMoversAndCaches()
    {
        _floatingCandidates.Clear();
        _nextIntersectCheck.Clear();
        _mapHasAdjacentLayerCache.Clear();
        _candidateRescanAccumulator = 0f;

        _entityBuffer.Clear();
        var moverQuery = EntityQueryEnumerator<ZSpaceMoverComponent>();
        while (moverQuery.MoveNext(out var uid, out _))
        {
            _entityBuffer.Add(uid);
        }

        foreach (var uid in _entityBuffer)
        {
            if (TryComp<ZSpaceMoverComponent>(uid, out var mover))
                RemoveMover(uid, mover);
        }
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

        if (direction > 0 && _zLevels.HasTileAbove(uid))
            return false;

        if (direction < 0 && _zLevels.HasTileBelow(uid))
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

        if (direction > 0 && _zLevels.HasTileAbove(uid))
            return false;

        if (direction < 0 && _zLevels.HasTileBelow(uid))
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
