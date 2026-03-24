using Content.Server.Doors.Systems;
using Content.Goobstation.Common.Effects;
using Content.Shared.Access.Systems;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.StationRecords;
using Robust.Shared.Map;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class CyberDeckScriptRemoteDeactivationSystem : EntitySystem
{
    private static readonly ICollection<StationRecordKey> EmptyStationKeys = Array.Empty<StationRecordKey>();
    private const LookupFlags AirlockLookupFlags =
        LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.StaticSundries | LookupFlags.Sundries;

    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly DoorSystem _doors = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SparksSystem _sparks = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CyberDeckScriptRemoteDeactivationComponent, CyberDeckScriptExecutedEvent>(OnExecuted);
        SubscribeLocalEvent<CyberDeckScriptRemoteDeactivationComponent, CyberDeckScriptRemoteDeactivationDoAfterEvent>(OnDoAfter);
    }

    private void OnExecuted(Entity<CyberDeckScriptRemoteDeactivationComponent> ent, ref CyberDeckScriptExecutedEvent args)
    {
        if (!TryResolveTargetAirlock(ent, args.TargetEntity, args.TargetCoordinates, out var target))
            return;

        if (!CanOperateDoor(args.Body, target, ent.Comp))
            return;

        _sparks.DoSparks(Transform(target).Coordinates);

        var delay = MathF.Max(0f, ent.Comp.OperationDelay);
        if (delay <= 0f)
        {
            ToggleDoor(target);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(
            EntityManager,
            args.Performer,
            delay,
            new CyberDeckScriptRemoteDeactivationDoAfterEvent
            {
                Target = GetNetEntity(target),
                Body = GetNetEntity(args.Body),
            },
            ent.Owner,
            target: target)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            DistanceThreshold = null,
            RequireCanInteract = false,
        });
    }

    private void OnDoAfter(
        Entity<CyberDeckScriptRemoteDeactivationComponent> ent,
        ref CyberDeckScriptRemoteDeactivationDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;

        var target = GetEntity(args.Target);
        var body = GetEntity(args.Body);

        if (!Exists(target) || !Exists(body))
            return;

        if (!CanOperateDoor(body, target, ent.Comp))
            return;

        ToggleDoor(target);
    }

    private bool TryResolveTargetAirlock(
        Entity<CyberDeckScriptRemoteDeactivationComponent> ent,
        EntityUid? targetEntity,
        EntityCoordinates? targetCoordinates,
        out EntityUid target)
    {
        target = default;

        if (targetEntity is { } directTarget &&
            Exists(directTarget) &&
            TryComp<AirlockComponent>(directTarget, out _) &&
            TryComp<DoorComponent>(directTarget, out _))
        {
            target = directTarget;
            return true;
        }

        if (targetCoordinates is not { } coords || !coords.IsValid(EntityManager))
            return false;

        var mapCoords = _transform.ToMapCoordinates(coords);
        var searchRadius = MathF.Max(0.05f, ent.Comp.TargetSearchRadius);
        var bestDistanceSquared = float.MaxValue;

        foreach (var (candidate, _) in _lookup.GetEntitiesInRange<AirlockComponent>(
                     coords,
                     searchRadius,
                     AirlockLookupFlags))
        {
            if (!TryComp<DoorComponent>(candidate, out _))
                continue;

            var candidateCoords = _transform.GetMapCoordinates(candidate);
            if (candidateCoords.MapId != mapCoords.MapId)
                continue;

            var distance = (candidateCoords.Position - mapCoords.Position).LengthSquared();
            if (distance >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distance;
            target = candidate;
        }

        return target != default;
    }

    private bool CanOperateDoor(
        EntityUid body,
        EntityUid target,
        CyberDeckScriptRemoteDeactivationComponent comp)
    {
        if (!TryComp<AirlockComponent>(target, out _)
            || !TryComp<DoorComponent>(target, out _))
        {
            return false;
        }

        if (!MatchesConfiguredAccess(target, comp))
            return false;

        var range = MathF.Max(0f, comp.Range);
        if (range <= 0f)
            return false;

        if (EntityManager.HasOperationalCyberneticOrgan<EyesComponent>(body))
            return _transform.InRange(body, target, range);

        return _interaction.InRangeUnobstructed(body, target, range, CollisionGroup.Opaque);
    }

    private bool MatchesConfiguredAccess(EntityUid target, CyberDeckScriptRemoteDeactivationComponent comp)
    {
        if (comp.Access.Count == 0)
            return true;

        var matches = true;
        if (_accessReader.GetMainAccessReader(target, out var readerEnt) &&
            readerEnt is { } reader)
        {
            matches = _accessReader.IsAllowed(comp.Access, EmptyStationKeys, reader.Owner, reader.Comp);
        }

        return comp.Inverted ? !matches : matches;
    }

    private void ToggleDoor(EntityUid target)
    {
        if (!TryComp<DoorComponent>(target, out var door))
            return;

        _doors.TryToggleDoor(target, door, null);
    }
}
