using Content.Shared._Onyx.Swimming.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Robust.Shared.Timing;
using Content.Shared.Ghost;

namespace Content.Shared._Onyx.Swimming.Systems;

public sealed class SharedOceanSwimmingSystem : EntitySystem
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(0.1);

    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly HashSet<EntityUid> _oceanMaps = new();

    private TimeSpan _nextRefresh;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OceanMapComponent, ComponentStartup>(OnOceanStartup);
        SubscribeLocalEvent<OceanMapComponent, ComponentShutdown>(OnOceanShutdown);
        SubscribeLocalEvent<OceanSwimmingComponent, CanWeightlessMoveEvent>(OnCanWeightlessMove);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_oceanMaps.Count == 0)
            return;

        var now = _timing.CurTime;
        if (now < _nextRefresh)
            return;

        _nextRefresh = now + RefreshInterval;

        var query = EntityQueryEnumerator<InputMoverComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            Refresh(uid, xform);
        }
    }

    private void OnOceanStartup(Entity<OceanMapComponent> ent, ref ComponentStartup args)
    {
        _oceanMaps.Add(ent.Owner);

        var query = EntityQueryEnumerator<InputMoverComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid == ent.Owner)
                Refresh(uid, xform);
        }
    }

    private void OnOceanShutdown(Entity<OceanMapComponent> ent, ref ComponentShutdown args)
    {
        _oceanMaps.Remove(ent.Owner);

        var query = EntityQueryEnumerator<OceanSwimmingComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid == ent.Owner)
                RemoveSwimming(uid);
        }
    }

    private void Refresh(EntityUid uid, TransformComponent xform)
    {
        if (ShouldIgnoreOceanSwimming(uid))
        {
            RemoveSwimming(uid);
            return;
        }

        if (xform.MapUid is not { } mapUid || !_oceanMaps.Contains(mapUid))
        {
            RemoveSwimming(uid);
            return;
        }

        var swimming =
            xform.GridUid == null &&
            xform.ParentUid == mapUid;

        if (!swimming)
        {
            RemoveSwimming(uid);
            return;
        }

        if (HasComp<OceanSwimmingComponent>(uid))
            return;

        var component = EnsureComp<OceanSwimmingComponent>(uid);
        component.NextStroke = _timing.CurTime;
    }

    private bool ShouldIgnoreOceanSwimming(EntityUid uid)
    {
        if (HasComp<GhostComponent>(uid))
            return true;

        if (HasComp<CanMoveInAirComponent>(uid))
            return true;

        return false;
    }

    private void RemoveSwimming(EntityUid uid)
    {
        if (HasComp<OceanSwimmingComponent>(uid))
            RemComp<OceanSwimmingComponent>(uid);
    }

    private void OnCanWeightlessMove(Entity<OceanSwimmingComponent> ent, ref CanWeightlessMoveEvent args)
    {
        args.CanMove = true;
    }
}