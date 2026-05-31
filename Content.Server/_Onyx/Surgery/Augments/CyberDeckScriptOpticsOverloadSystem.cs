using System.Collections.Generic;
using Content.Server.Body.Systems;
using Content.Server.Emp;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._Shitmed.Cybernetics;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class CyberDeckScriptOpticsOverloadSystem : EntitySystem
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
    private readonly HashSet<Entity<BodyComponent>> _bodyCandidates = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CyberDeckScriptOpticsOverloadComponent, CyberDeckScriptExecutedEvent>(OnExecuted);
        SubscribeLocalEvent<CyberDeckScriptOpticsOverloadComponent, CyberDeckScriptOpticsOverloadDoAfterEvent>(OnDoAfter);
    }

    private void OnExecuted(Entity<CyberDeckScriptOpticsOverloadComponent> ent, ref CyberDeckScriptExecutedEvent args)
    {
        if (!TryResolveTarget(ent.Comp, args.TargetEntity, args.TargetCoordinates, out var target))
            return;

        if (!CanOperateTarget(args.Body, target, ent.Comp))
            return;

        var delay = MathF.Max(0f, ent.Comp.OperationDelay * MathF.Max(1f, args.AciTimeMultiplier));
        if (delay <= 0f)
        {
            DisableTargetOptics(target, ent.Comp);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(
            EntityManager,
            args.Performer,
            delay,
            new CyberDeckScriptOpticsOverloadDoAfterEvent
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
        Entity<CyberDeckScriptOpticsOverloadComponent> ent,
        ref CyberDeckScriptOpticsOverloadDoAfterEvent args)
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

        DisableTargetOptics(target, ent.Comp);
    }

    private bool TryResolveTarget(
        CyberDeckScriptOpticsOverloadComponent comp,
        EntityUid? targetEntity,
        EntityCoordinates? targetCoordinates,
        out EntityUid target)
    {
        target = default;

        if (targetEntity is { } directTarget &&
            Exists(directTarget) &&
            IsValidOpticalTarget(directTarget))
        {
            target = directTarget;
            return true;
        }

        if (targetCoordinates is not { } coords || !coords.IsValid(EntityManager))
            return false;

        var mapCoords = _transform.ToMapCoordinates(coords);
        var searchRadius = MathF.Max(0.05f, comp.TargetSearchRadius);
        var bestDistanceSquared = float.MaxValue;

        _bodyCandidates.Clear();
        _lookup.GetEntitiesInRange(coords, searchRadius, _bodyCandidates, TargetLookupFlags);
        foreach (var (candidate, _) in _bodyCandidates)
        {
            if (!IsValidOpticalTarget(candidate))
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
        CyberDeckScriptOpticsOverloadComponent comp)
    {
        if (!IsValidOpticalTarget(targetBody))
            return false;

        var hasOptics = EntityManager.HasOperationalCyberneticOrgan<EyesComponent>(userBody);
        var range = hasOptics ? comp.Range : comp.RangeWithoutOptics;
        range = MathF.Max(0f, range);

        if (range <= 0f)
            return false;

        if (hasOptics)
            return _transform.InRange(userBody, targetBody, range);

        return _interaction.InRangeUnobstructed(userBody, targetBody, range, CollisionGroup.Opaque);
    }

    private bool IsValidOpticalTarget(EntityUid target)
    {
        return EntityManager.HasOperationalCyberneticOrgan<EyesComponent>(target);
    }

    private bool DisableTargetOptics(EntityUid targetBody, CyberDeckScriptOpticsOverloadComponent comp)
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
        foreach (var (organUid, organComp) in _body.GetBodyOrgans(targetBody))
        {
            if (!organComp.Enabled)
                continue;

            if (!TryComp<CyberneticsComponent>(organUid, out var cyberComp) || cyberComp.Disabled)
                continue;

            if (!TryComp<EyesComponent>(organUid, out _))
                continue;

            if (CyberDeckScriptDisruptionHelper.TryDisableCybernetic(
                    organUid,
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
            _popup.PopupEntity(Loc.GetString("cyberdeck-script-popup-optics-overload"), targetBody, targetBody, PopupType.LargeCaution);
        }

        return affected;
    }
}
