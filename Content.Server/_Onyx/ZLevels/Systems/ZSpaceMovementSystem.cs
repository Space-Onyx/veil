using Content.Server.Actions;
using Content.Shared._Onyx.ZLevels.Core.Components;
using Content.Shared._Onyx.ZLevels.Core.EntitySystems;
using Content.Shared._Onyx.ZLevels;
using Content.Shared._Onyx.ZLevels.Components;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Onyx.ZLevels.Systems;

public sealed class ZSpaceMovementSystem : EntitySystem
{
    [Dependency] private readonly CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private const float MoveDuration = 1f;
    private const float CheckInterval = 0.5f;
    private const float CandidateRescanInterval = 10f;

    private float _accumulator;
    private float _candidateRescanAccumulator;
    private int _zNetworkCount;
    private readonly Dictionary<EntityUid, bool> _mapHasAdjacentLayerCache = new();
    private bool _adjacentLayerCacheDirty = true;
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

        _candidateRescanAccumulator += frameTime;
        if (_candidateRescanAccumulator >= CandidateRescanInterval)
        {
            _candidateRescanAccumulator = 0f;
            RefreshNetworkCount();
            RebuildFloatingCandidates();
        }

        if (_zNetworkCount <= 0)
            return;

        _accumulator += frameTime;
        if (_accumulator < CheckInterval)
            return;
        _accumulator -= CheckInterval;
        _adjacentLayerCacheDirty = true;

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
            var hasZPhysics = TryComp<CEZPhysicsComponent>(uid, out var zPhysics);

            if (xform.GridUid != null || xform.MapUid == null)
            {
                if (hasMover)
                    RemoveFloatingCandidate(uid, spaceMover!);
                else
                    RemoveFloatingCandidate(uid);

                if (hasZPhysics && zPhysics!.GravityMultiplier == 0f)
                    _zLevels.SetZGravity((uid, zPhysics), 1f);

                continue;
            }

            var mapUid = xform.MapUid.Value;
            if (!HasAdjacentZLayer(mapUid))
            {
                if (hasMover)
                    RemoveFloatingCandidate(uid, spaceMover!);
                else
                    RemoveFloatingCandidate(uid);

                if (hasZPhysics && zPhysics!.GravityMultiplier == 0f)
                    _zLevels.SetZGravity((uid, zPhysics), 1f);

                continue;
            }

            var worldPos = _transform.GetWorldPosition(xform);
            var mapHasGravity = HasGravityOnMap(xform);

            if (_mapManager.TryFindGridAt(mapUid, worldPos, out _, out _))
            {
                if (hasMover)
                    RemoveMover(uid, spaceMover!);

                if (hasZPhysics)
                {
                    if (mapHasGravity && zPhysics!.GravityMultiplier == 0f)
                        _zLevels.SetZGravity((uid, zPhysics), 1f);
                    else if (!mapHasGravity && zPhysics!.GravityMultiplier != 0f)
                        _zLevels.SetZGravity((uid, zPhysics), 0f);
                }

                continue;
            }

            if (_zLevels.IsEntityOverInteriorHole(xform))
            {
                if (hasMover)
                    RemoveMover(uid, spaceMover!);

                if (hasZPhysics && zPhysics!.GravityMultiplier == 0f)
                    _zLevels.SetZGravity((uid, zPhysics), 1f);

                continue;
            }

            if (mapHasGravity)
            {
                if (hasMover)
                    RemoveMover(uid, spaceMover!);

                if (hasZPhysics && zPhysics!.GravityMultiplier == 0f)
                    _zLevels.SetZGravity((uid, zPhysics), 1f);
            }
            else
            {
                if (!hasMover)
                    AddMover(uid);

                if (hasZPhysics && zPhysics!.GravityMultiplier != 0f)
                    _zLevels.SetZGravity((uid, zPhysics), 0f);
            }
        }
    }

    private bool HasAdjacentZLayer(EntityUid mapUid)
    {
        if (_adjacentLayerCacheDirty)
        {
            _mapHasAdjacentLayerCache.Clear();
            _adjacentLayerCacheDirty = false;
        }

        if (_mapHasAdjacentLayerCache.TryGetValue(mapUid, out var hasAdjacent))
            return hasAdjacent;

        hasAdjacent = _zLevels.TryMapOffset(mapUid, 1, out _) || _zLevels.TryMapOffset(mapUid, -1, out _);
        _mapHasAdjacentLayerCache[mapUid] = hasAdjacent;
        return hasAdjacent;
    }

    private bool HasGravityOnMap(TransformComponent xform)
    {
        if (xform.MapUid is not { } mapUid)
            return false;

        if (!TryComp<MapComponent>(mapUid, out var mapComp))
            return false;

        return _zLevels.HasMapEntityGravity(mapComp.MapId);
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
    }

    private void OnRemove(EntityUid uid, ZSpaceMoverComponent comp, ComponentRemove args)
    {
        _actions.RemoveAction(comp.UpActionEntity);
        _actions.RemoveAction(comp.DownActionEntity);
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
        _mapHasAdjacentLayerCache.Clear();
        _adjacentLayerCacheDirty = true;
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
        {
            _popup.PopupEntity(Loc.GetString("z-space-move-blocked-above"), uid, uid, PopupType.SmallCaution);
            return false;
        }

        if (direction < 0 && _zLevels.HasTileBelow(uid))
        {
            _popup.PopupEntity(Loc.GetString("z-space-move-blocked-below"), uid, uid, PopupType.SmallCaution);
            return false;
        }

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
        {
            _popup.PopupEntity(Loc.GetString("z-space-move-blocked-above"), uid, uid, PopupType.SmallCaution);
            return false;
        }

        if (direction < 0 && _zLevels.HasTileBelow(uid))
        {
            _popup.PopupEntity(Loc.GetString("z-space-move-blocked-below"), uid, uid, PopupType.SmallCaution);
            return false;
        }

        if (!_zLevels.TryMove(uid, direction))
            return false;

        if (TryComp<CEZPhysicsComponent>(uid, out var zPhys))
        {
            _zLevels.SetZVelocity((uid, zPhys), 0);
            _zLevels.SetZPosition((uid, zPhys), 0.5f);
        }

        return true;
    }
}
