using Content.Server.Doors.Systems;
using Content.Server.Emp;
using Content.Server.SurveillanceCamera;
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
using Robust.Shared.Random;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class CyberDeckScriptRemoteDeactivationSystem : EntitySystem
{
    private static readonly ICollection<StationRecordKey> EmptyStationKeys = Array.Empty<StationRecordKey>();
    private const LookupFlags TargetLookupFlags =
        LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.StaticSundries | LookupFlags.Sundries;

    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly DoorSystem _doors = default!;
    [Dependency] private readonly EmpSystem _emp = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
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
        if (!TryResolveTarget(ent, args.TargetEntity, args.TargetCoordinates, out var target, out var targetType))
            return;

        if (!CanOperateTarget(args.Body, target, targetType, ent.Comp))
            return;

        _sparks.DoSparks(Transform(target).Coordinates);

        var delay = MathF.Max(0f, ent.Comp.OperationDelay * MathF.Max(1f, args.AciTimeMultiplier));
        if (delay <= 0f)
        {
            ExecuteTarget(target, targetType, ent.Comp);
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

        if (!TryGetTargetType(target, out var targetType))
            return;

        if (!CanOperateTarget(body, target, targetType, ent.Comp))
            return;

        ExecuteTarget(target, targetType, ent.Comp);
    }

    private bool TryResolveTarget(
        Entity<CyberDeckScriptRemoteDeactivationComponent> ent,
        EntityUid? targetEntity,
        EntityCoordinates? targetCoordinates,
        out EntityUid target,
        out RemoteDeactivationTargetType targetType)
    {
        target = default;
        targetType = RemoteDeactivationTargetType.None;

        if (targetEntity is { } directTarget &&
            Exists(directTarget) &&
            TryGetTargetType(directTarget, out var directType))
        {
            target = directTarget;
            targetType = directType;
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
                     TargetLookupFlags))
        {
            if (!TryGetTargetType(candidate, out var candidateType))
                continue;

            var candidateCoords = _transform.GetMapCoordinates(candidate);
            if (candidateCoords.MapId != mapCoords.MapId)
                continue;

            var distance = (candidateCoords.Position - mapCoords.Position).LengthSquared();
            if (distance >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distance;
            target = candidate;
            targetType = candidateType;
        }

        foreach (var (candidate, _) in _lookup.GetEntitiesInRange<CyberDeckRemoteDeactivationCameraTargetComponent>(
                     coords,
                     searchRadius,
                     TargetLookupFlags))
        {
            if (!TryGetTargetType(candidate, out var candidateType))
                continue;

            var candidateCoords = _transform.GetMapCoordinates(candidate);
            if (candidateCoords.MapId != mapCoords.MapId)
                continue;

            var distance = (candidateCoords.Position - mapCoords.Position).LengthSquared();
            if (distance >= bestDistanceSquared)
                continue;

            bestDistanceSquared = distance;
            target = candidate;
            targetType = candidateType;
        }

        return target != default;
    }

    private bool CanOperateTarget(
        EntityUid body,
        EntityUid target,
        RemoteDeactivationTargetType targetType,
        CyberDeckScriptRemoteDeactivationComponent comp)
    {
        if (!IsTargetType(target, targetType))
            return false;

        if (targetType == RemoteDeactivationTargetType.Airlock && !MatchesConfiguredAccess(target, comp))
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

    private void ExecuteTarget(
        EntityUid target,
        RemoteDeactivationTargetType targetType,
        CyberDeckScriptRemoteDeactivationComponent comp)
    {
        switch (targetType)
        {
            case RemoteDeactivationTargetType.Airlock:
                ToggleDoor(target);
                break;
            case RemoteDeactivationTargetType.Camera:
                DisableCamera(target, comp);
                break;
        }
    }

    private void ToggleDoor(EntityUid target)
    {
        if (!TryComp<DoorComponent>(target, out var door))
            return;

        _doors.TryToggleDoor(target, door, null);
    }

    private void DisableCamera(EntityUid target, CyberDeckScriptRemoteDeactivationComponent comp)
    {
        if (!TryComp<SurveillanceCameraComponent>(target, out _))
            return;

        var minDuration = MathF.Min(comp.MinCameraDisableDuration, comp.MaxCameraDisableDuration);
        var maxDuration = MathF.Max(comp.MinCameraDisableDuration, comp.MaxCameraDisableDuration);

        minDuration = MathF.Max(0f, minDuration);
        maxDuration = MathF.Max(minDuration, maxDuration);
        if (maxDuration <= 0f)
            return;

        var duration = maxDuration > minDuration
            ? _random.NextFloat(minDuration, maxDuration)
            : minDuration;

        _emp.DoEmpEffects(target, 0f, duration);
    }

    private bool TryGetTargetType(EntityUid target, out RemoteDeactivationTargetType targetType)
    {
        if (TryComp<AirlockComponent>(target, out _) && TryComp<DoorComponent>(target, out _))
        {
            targetType = RemoteDeactivationTargetType.Airlock;
            return true;
        }

        if (TryComp<CyberDeckRemoteDeactivationCameraTargetComponent>(target, out _)
            && TryComp<SurveillanceCameraComponent>(target, out _)
            && Transform(target).Anchored)
        {
            targetType = RemoteDeactivationTargetType.Camera;
            return true;
        }

        targetType = RemoteDeactivationTargetType.None;
        return false;
    }

    private bool IsTargetType(EntityUid target, RemoteDeactivationTargetType targetType)
    {
        return targetType switch
        {
            RemoteDeactivationTargetType.Airlock => TryComp<AirlockComponent>(target, out _) && TryComp<DoorComponent>(target, out _),
            RemoteDeactivationTargetType.Camera => TryComp<CyberDeckRemoteDeactivationCameraTargetComponent>(target, out _) && TryComp<SurveillanceCameraComponent>(target, out _),
            _ => false,
        };
    }

    private enum RemoteDeactivationTargetType : byte
    {
        None,
        Airlock,
        Camera,
    }
}
