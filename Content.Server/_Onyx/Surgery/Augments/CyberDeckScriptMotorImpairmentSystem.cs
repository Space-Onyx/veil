using Content.Server.Body.Systems;
using Content.Server.Emp;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared._Shitmed.Cybernetics;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class CyberDeckScriptMotorImpairmentSystem : EntitySystem
{
    private const LookupFlags TargetLookupFlags =
        LookupFlags.Dynamic | LookupFlags.StaticSundries | LookupFlags.Sensors;

    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly EmpSystem _emp = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CyberDeckScriptMotorImpairmentComponent, CyberDeckScriptExecutedEvent>(OnExecuted);
        SubscribeLocalEvent<CyberDeckScriptMotorImpairmentComponent, CyberDeckScriptMotorImpairmentDoAfterEvent>(OnDoAfter);
    }

    private void OnExecuted(Entity<CyberDeckScriptMotorImpairmentComponent> ent, ref CyberDeckScriptExecutedEvent args)
    {
        if (!TryResolveTarget(ent.Comp, args.TargetEntity, args.TargetCoordinates, out var target))
            return;

        if (!CanOperateTarget(args.Body, target, ent.Comp))
            return;

        var delay = MathF.Max(0f, ent.Comp.OperationDelay * MathF.Max(1f, args.AciTimeMultiplier));
        if (delay <= 0f)
        {
            DisableTargetMotorics(target, ent.Comp);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(
            EntityManager,
            args.Performer,
            delay,
            new CyberDeckScriptMotorImpairmentDoAfterEvent
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
        Entity<CyberDeckScriptMotorImpairmentComponent> ent,
        ref CyberDeckScriptMotorImpairmentDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;

        var body = GetEntity(args.Body);
        var target = GetEntity(args.Target);

        if (!Exists(body) || !Exists(target))
            return;

        if (!CanOperateTarget(body, target, ent.Comp))
            return;

        DisableTargetMotorics(target, ent.Comp);
    }

    private bool TryResolveTarget(
        CyberDeckScriptMotorImpairmentComponent comp,
        EntityUid? targetEntity,
        EntityCoordinates? targetCoordinates,
        out EntityUid target)
    {
        target = default;

        if (targetEntity is { } directTarget &&
            Exists(directTarget) &&
            IsValidMotoricsTarget(directTarget))
        {
            target = directTarget;
            return true;
        }

        if (targetCoordinates is not { } coords || !coords.IsValid(EntityManager))
            return false;

        var mapCoords = _transform.ToMapCoordinates(coords);
        var searchRadius = MathF.Max(0.05f, comp.TargetSearchRadius);
        var bestDistanceSquared = float.MaxValue;

        foreach (var (candidate, _) in _lookup.GetEntitiesInRange<BodyComponent>(
                     coords,
                     searchRadius,
                     TargetLookupFlags))
        {
            if (!IsValidMotoricsTarget(candidate))
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

    private bool CanOperateTarget(
        EntityUid userBody,
        EntityUid targetBody,
        CyberDeckScriptMotorImpairmentComponent comp)
    {
        if (!IsValidMotoricsTarget(targetBody))
            return false;

        var range = MathF.Max(0f, comp.Range);
        if (range <= 0f)
            return false;

        return _interaction.InRangeUnobstructed(userBody, targetBody, range, CollisionGroup.Opaque);
    }

    private bool IsValidMotoricsTarget(EntityUid targetBody)
    {
        foreach (var (partUid, partComp) in _body.GetBodyChildren(targetBody))
        {
            if (partComp.PartType != BodyPartType.Leg)
                continue;

            if (!TryComp<CyberneticsComponent>(partUid, out var cyberComp) || cyberComp.Disabled)
                continue;

            return true;
        }

        return false;
    }

    private bool DisableTargetMotorics(EntityUid targetBody, CyberDeckScriptMotorImpairmentComponent comp)
    {
        var minDuration = MathF.Min(comp.MinDisableDuration, comp.MaxDisableDuration);
        var maxDuration = MathF.Max(comp.MinDisableDuration, comp.MaxDisableDuration);

        minDuration = MathF.Max(0f, minDuration);
        maxDuration = MathF.Max(minDuration, maxDuration);
        if (maxDuration <= 0f)
            return false;

        var duration = maxDuration > minDuration
            ? _random.NextFloat(minDuration, maxDuration)
            : minDuration;

        var disabledUntil = _timing.CurTime + TimeSpan.FromSeconds(duration);
        var affected = false;

        foreach (var (partUid, partComp) in _body.GetBodyChildren(targetBody))
        {
            if (partComp.PartType != BodyPartType.Leg)
                continue;

            if (!CyberDeckScriptDisruptionHelper.IsCyberneticBodyPartType(partUid, BodyPartType.Leg, EntityManager))
                continue;

            if (CyberDeckScriptDisruptionHelper.TryDisableCybernetic(
                    partUid,
                    disabledUntil,
                    _emp,
                    _timing,
                    EntityManager))
            {
                affected = true;
            }
        }

        if (affected)
        {
            Spawn("EffectSparks", Transform(targetBody).Coordinates);
            _popup.PopupEntity(Loc.GetString("cyberdeck-script-popup-motor-impairment"), targetBody, targetBody, PopupType.LargeCaution);
        }

        return affected;
    }
}
