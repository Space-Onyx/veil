using System.Collections.Generic;
using Content.Goobstation.Shared.Augments;
using Content.Server.Body.Systems;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Body.Components;
using Content.Shared.UserInterface;
using Robust.Shared.Map;
using Content.Shared.Popups;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class CyberDeckScriptSystem : EntitySystem
{
    private const float AciRamPenaltyMultiplier = 1.5f;
    private const float AciTimePenaltyMultiplier = 2f;
    private const float AciLookupRadius = 1.2f;
    private const LookupFlags AciLookupFlags =
        LookupFlags.Dynamic | LookupFlags.Static | LookupFlags.StaticSundries | LookupFlags.Sundries;

    [Dependency] private readonly AugmentSystem _augment = default!;
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly CyberDeckSystem _cyberDeck = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    private readonly HashSet<Entity<AciProtectionComponent>> _aciCandidates = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CyberDeckScriptComponent, CyberDeckScriptActionEvent>(OnScriptAction);
        SubscribeLocalEvent<CyberDeckScriptComponent, CyberDeckScriptTargetActionEvent>(OnScriptTargetAction);
    }
    public void OnScriptInserted(EntityUid moduleUid, CyberDeckScriptComponent script, EntityUid body)
    {
        EnsureComp<ActionsContainerComponent>(moduleUid);
        _actions.AddAction(body, ref script.ActionEntity, script.Action, moduleUid);
    }
    public void OnScriptRemoved(EntityUid moduleUid, CyberDeckScriptComponent script, EntityUid? body)
    {
        if (body != null)
            _actions.RemoveAction(body.Value, script.ActionEntity);
        else if (script.ActionEntity != null)
            QueueDel(script.ActionEntity.Value);

        script.ActionEntity = null;
    }
    private void OnScriptAction(Entity<CyberDeckScriptComponent> ent, ref CyberDeckScriptActionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryPrepareExecution(ent, ent.Comp.RamCost, out var parent, out var body))
            return;

        if (TryComp<CyberDeckScriptActivatableUIComponent>(ent, out var uiComp) &&
            uiComp.Key is { } key &&
            _ui.HasUi(ent, key))
        {
            if (_ui.TryOpenUi(ent.Owner, key, body))
            {
                var opened = new AfterActivatableUIOpenEvent(body, body);
                RaiseLocalEvent(ent.Owner, opened);
            }
        }

        var executed = new CyberDeckScriptExecutedEvent(body, parent, args.Performer);
        RaiseLocalEvent(ent.Owner, ref executed);

        args.Handled = true;
    }

    private void OnScriptTargetAction(Entity<CyberDeckScriptComponent> ent, ref CyberDeckScriptTargetActionEvent args)
    {
        if (args.Handled)
            return;

        var aciResult = EvaluateAci(ent.Comp, args.Entity, args.Target);
        if (aciResult.Blocked)
        {
            var bodyUid = _augment.GetBody(Transform(ent).ParentUid);
            if (bodyUid is { } blockedBody)
            {
                _popup.PopupEntity(
                    Loc.GetString("cyberdeck-script-aci-blocked"),
                    blockedBody,
                    blockedBody,
                    PopupType.SmallCaution);
            }

            return;
        }

        var requiredRam = ent.Comp.RamCost * aciResult.RamMultiplier;
        if (!TryPrepareExecution(ent, requiredRam, out var parent, out var body))
            return;

        var executed = new CyberDeckScriptExecutedEvent(
            body,
            parent,
            args.Performer,
            args.Entity,
            args.Target,
            aciResult.TimeMultiplier);
        RaiseLocalEvent(ent.Owner, ref executed);

        args.Handled = true;
    }

    private bool TryPrepareExecution(
        Entity<CyberDeckScriptComponent> ent,
        float requiredRam,
        out EntityUid cyberDeck,
        out EntityUid body)
    {
        cyberDeck = default;
        body = default;

        cyberDeck = Transform(ent).ParentUid;
        if (!TryComp<CyberDeckComponent>(cyberDeck, out var deckComp))
            return false;

        var bodyUid = _augment.GetBody(cyberDeck);
        if (bodyUid == null)
            return false;

        body = bodyUid.Value;

        var ramCost = MathF.Max(0f, requiredRam);
        if (_cyberDeck.TrySpendRam(cyberDeck, ramCost, deckComp))
            return true;

        _popup.PopupEntity(
            Loc.GetString("cyberdeck-script-not-enough-ram"),
            body,
            body,
            PopupType.SmallCaution);

        return false;
    }

    private AciExecutionResult EvaluateAci(
        CyberDeckScriptComponent script,
        EntityUid? targetEntity,
        EntityCoordinates? targetCoordinates)
    {
        if (!TryResolveAciTarget(targetEntity, targetCoordinates, out var target))
            return AciExecutionResult.Normal;

        var protection = GetAciProtectionLevel(target);
        var penetration = Math.Max(0, script.AciPenetrationLevel);

        if (protection <= penetration)
            return AciExecutionResult.Normal;

        var difference = protection - penetration;
        if (difference >= 2)
            return AciExecutionResult.BlockedResult;

        return new AciExecutionResult(false, AciRamPenaltyMultiplier, AciTimePenaltyMultiplier);
    }

    private bool TryResolveAciTarget(
        EntityUid? targetEntity,
        EntityCoordinates? targetCoordinates,
        out EntityUid target)
    {
        target = default;

        if (targetEntity is { } directTarget && Exists(directTarget))
        {
            target = directTarget;
            return true;
        }

        if (targetCoordinates is not { } coords || !coords.IsValid(EntityManager))
            return false;

        var mapCoords = _transform.ToMapCoordinates(coords);
        var bestDistanceSquared = float.MaxValue;

        _aciCandidates.Clear();
        _lookup.GetEntitiesInRange(coords, AciLookupRadius, _aciCandidates, AciLookupFlags);
        foreach (var (candidate, _) in _aciCandidates)
        {
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

    private int GetAciProtectionLevel(EntityUid target)
    {
        var body = TryComp<BodyComponent>(target, out _)
            ? target
            : _augment.GetBody(target);

        if (body is { } bodyUid && TryGetBodyNeuroInterfaceProtection(bodyUid, out var neuroProtection))
            return neuroProtection;

        if (TryComp<AciProtectionComponent>(target, out var aci))
            return Math.Max(0, aci.Level);

        if (TryComp<AugmentNeuroInterfaceComponent>(target, out var neuro))
            return Math.Max(0, neuro.AciProtectionLevel);

        return 0;
    }

    private bool TryGetBodyNeuroInterfaceProtection(EntityUid body, out int protection)
    {
        protection = 0;

        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            foreach (var (organUid, _) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!TryComp<AugmentNeuroInterfaceComponent>(organUid, out var neuro))
                    continue;

                protection = Math.Max(0, neuro.AciProtectionLevel);

                if (TryComp<AciProtectionComponent>(organUid, out var aci))
                    protection = Math.Max(protection, Math.Max(0, aci.Level));

                return true;
            }
        }

        return false;
    }

    private readonly record struct AciExecutionResult(bool Blocked, float RamMultiplier, float TimeMultiplier)
    {
        public static readonly AciExecutionResult Normal = new(false, 1f, 1f);
        public static readonly AciExecutionResult BlockedResult = new(true, 1f, 1f);
    }
}
