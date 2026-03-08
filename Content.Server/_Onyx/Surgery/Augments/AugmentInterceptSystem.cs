using Content.Goobstation.Shared.Augments;
using Content.Server.Power.Components;
using Content.Server.PowerCell;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Actions;
using Content.Shared.Body.Events;
using Content.Shared.Popups;
using Content.Shared.PowerCell;
using Robust.Server.GameObjects;
using Robust.Shared.Utility;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentInterceptSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly AugmentSystem _augment = default!;
    [Dependency] private readonly SharedAugmentPowerCellSystem _augmentPower = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private const float InterceptRange = 20f;
    private const string InterceptAction = "ActionAugmentIntercept";
    private const string ReconnectAction = "ActionAugmentInterceptReconnect";
    private const string InterceptPrototype = "AugmentIntercept";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentInterceptComponent, OrganAddedToBodyEvent>(OnOrganAddedToBody);
        SubscribeLocalEvent<AugmentInterceptComponent, OrganRemovedFromBodyEvent>(OnOrganRemovedFromBody);
        SubscribeLocalEvent<AugmentInterceptComponent, ComponentStartup>(OnComponentStartup);
        SubscribeLocalEvent<AugmentInterceptComponent, AugmentInterceptTargetActionEvent>(OnTargetIntercept);
        SubscribeLocalEvent<AugmentInterceptComponent, AugmentInterceptReconnectActionEvent>(OnReconnectIntercept);
        SubscribeLocalEvent<AugmentInterceptComponent, CollectAugmentNeuroInterfaceMetricsEvent>(OnCollectMetrics);
    }

    private void OnComponentStartup(Entity<AugmentInterceptComponent> ent, ref ComponentStartup args)
    {
        if (_augment.GetBody(ent) is not { } body)
            return;

        if (ent.Comp.ReconnectActionEntity is { } reconnect)
            _actions.RemoveAction(body, reconnect);

        ent.Comp.ReconnectActionEntity = null;
        if (ent.Comp.InterceptActionEntity is { } existing)
            _actions.RemoveAction(body, existing);

        ent.Comp.InterceptActionEntity = null;
        _actions.AddAction(body, ref ent.Comp.InterceptActionEntity, InterceptAction, ent);
        if (ent.Comp.InterceptActionEntity is { } action)
            _actions.SetIcon(action, new SpriteSpecifier.EntityPrototype(InterceptPrototype));
    }

    private void OnOrganAddedToBody(Entity<AugmentInterceptComponent> ent, ref OrganAddedToBodyEvent args)
    {
        if (ent.Comp.InterceptActionEntity is { } existing)
            _actions.RemoveAction(args.Body, existing);

        ent.Comp.InterceptActionEntity = null;
        _actions.AddAction(args.Body, ref ent.Comp.InterceptActionEntity, InterceptAction, ent);
        if (ent.Comp.InterceptActionEntity is { } action)
            _actions.SetIcon(action, new SpriteSpecifier.EntityPrototype(InterceptPrototype));
    }

    private void OnOrganRemovedFromBody(Entity<AugmentInterceptComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        if (ent.Comp.InterceptActionEntity.HasValue)
        {
            _actions.RemoveAction(args.OldBody, ent.Comp.InterceptActionEntity.Value);
            ent.Comp.InterceptActionEntity = null;
        }

        if (ent.Comp.ReconnectActionEntity.HasValue)
        {
            _actions.RemoveAction(args.OldBody, ent.Comp.ReconnectActionEntity.Value);
            ent.Comp.ReconnectActionEntity = null;
        }

        ent.Comp.LastInterceptedBody = null;
        ent.Comp.LastInterceptedInterface = null;
    }

    private void OnTargetIntercept(Entity<AugmentInterceptComponent> ent, ref AugmentInterceptTargetActionEvent args)
    {
        if (args.Handled)
            return;

        if (_augment.GetBody(ent) is not { } body || body != args.Performer)
            return;

        if (!CanUseIntercept(ent, body))
            return;

        var targetBody = ResolveTargetBody(args.Target);
        if (!TryGetNeuroInterface(targetBody, out var neuroInterface))
        {
            _popup.PopupEntity(Loc.GetString("augment-intercept-popup-no-interface"), body, body, PopupType.SmallCaution);
            return;
        }

        if (!IsBodyInRange(body, targetBody))
        {
            _popup.PopupEntity(Loc.GetString("augment-intercept-popup-too-far"), body, body, PopupType.SmallCaution);
            return;
        }

        if (!OpenRemoteNeuroInterface(body, neuroInterface))
        {
            _popup.PopupEntity(Loc.GetString("augment-intercept-popup-open-failed"), body, body, PopupType.SmallCaution);
            return;
        }

        ent.Comp.LastInterceptedBody = targetBody;
        ent.Comp.LastInterceptedInterface = neuroInterface;
        _actions.AddAction(body, ref ent.Comp.ReconnectActionEntity, ReconnectAction, ent);
        if (ent.Comp.ReconnectActionEntity is { } reconnectAction)
            _actions.SetIcon(reconnectAction, new SpriteSpecifier.EntityPrototype(InterceptPrototype));
        _popup.PopupEntity(Loc.GetString("augment-intercept-popup-linked"), body, body, PopupType.Medium);
        args.Handled = true;
    }

    private void OnReconnectIntercept(Entity<AugmentInterceptComponent> ent, ref AugmentInterceptReconnectActionEvent args)
    {
        if (args.Handled)
            return;

        if (_augment.GetBody(ent) is not { } body || body != args.Performer)
            return;

        if (!CanUseIntercept(ent, body))
            return;

        if (ent.Comp.LastInterceptedBody is not { } targetBody
            || ent.Comp.LastInterceptedInterface is not { } neuroInterface
            || Deleted(targetBody)
            || Deleted(neuroInterface))
        {
            _popup.PopupEntity(Loc.GetString("augment-intercept-popup-no-link"), body, body, PopupType.SmallCaution);
            return;
        }

        if (!TryComp<AugmentNeuroInterfaceComponent>(neuroInterface, out _))
        {
            _popup.PopupEntity(Loc.GetString("augment-intercept-popup-no-link"), body, body, PopupType.SmallCaution);
            return;
        }

        if (!IsBodyInRange(body, targetBody))
        {
            _popup.PopupEntity(Loc.GetString("augment-intercept-popup-too-far"), body, body, PopupType.SmallCaution);
            return;
        }

        if (!OpenRemoteNeuroInterface(body, neuroInterface))
            _popup.PopupEntity(Loc.GetString("augment-intercept-popup-open-failed"), body, body, PopupType.SmallCaution);

        args.Handled = true;
    }

    private bool CanUseIntercept(Entity<AugmentInterceptComponent> ent, EntityUid body)
    {
        if (HasComp<AugmentNeuroManuallyDisabledComponent>(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("augment-disabled-manually"), body, body, PopupType.SmallCaution);
            return false;
        }

        if (HasComp<AugmentBrainDeactivatedComponent>(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("augment-brain-disabled"), body, body, PopupType.SmallCaution);
            return false;
        }

        if (HasComp<AugmentEmpDisabledComponent>(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("augment-emp-disabled"), body, body, PopupType.SmallCaution);
            return false;
        }

        if (TryComp<AugmentPowerConfigComponent>(ent.Owner, out var powerConfig) && !powerConfig.RequiresPower)
            return true;

        if (!HasAugmentPower(body))
        {
            _popup.PopupEntity(Loc.GetString("augment-no-power"), body, body, PopupType.SmallCaution);
            return false;
        }

        return true;
    }

    private bool HasAugmentPower(EntityUid body)
    {
        if (_augmentPower.GetBodyAugment(body) is not { } slot)
            return false;

        if (!TryComp<PowerCellDrawComponent>(slot, out var draw))
            return false;

        return _powerCell.HasDrawCharge(slot, draw);
    }

    private bool TryGetNeuroInterface(EntityUid body, out EntityUid neuroInterface)
    {
        neuroInterface = default;

        if (!TryComp<InstalledAugmentsComponent>(body, out var installed))
            return false;

        foreach (var netAug in installed.InstalledAugments)
        {
            var augment = GetEntity(netAug);
            if (!TryComp<AugmentNeuroInterfaceComponent>(augment, out _))
                continue;

            neuroInterface = augment;
            return true;
        }

        return false;
    }

    private bool IsBodyInRange(EntityUid sourceBody, EntityUid targetBody)
    {
        var sourceCoords = Transform(sourceBody).Coordinates;
        var targetCoords = Transform(targetBody).Coordinates;
        return sourceCoords.TryDistance(EntityManager, targetCoords, out var distance) && distance <= InterceptRange;
    }

    private EntityUid ResolveTargetBody(EntityUid target)
    {
        var current = target;
        while (true)
        {
            if (HasComp<InstalledAugmentsComponent>(current))
                return current;

            var xform = Transform(current);
            if (!xform.ParentUid.IsValid() || xform.ParentUid == current)
                return target;

            current = xform.ParentUid;
        }
    }

    private bool OpenRemoteNeuroInterface(EntityUid user, EntityUid targetInterface)
    {
        if (!TryComp<AugmentNeuroInterfaceComponent>(targetInterface, out var neuroComp))
            return false;

        if (!_ui.HasUi(targetInterface, NeuroInterfaceUiKey.Key))
            return false;

        neuroComp.AuthorizedRemoteViewers.Add(user);
        _ui.OpenUi(targetInterface, NeuroInterfaceUiKey.Key, user);
        return true;
    }

    private void OnCollectMetrics(Entity<AugmentInterceptComponent> ent, ref CollectAugmentNeuroInterfaceMetricsEvent args)
    {
        if (args.PowerEnabled && ent.Comp.ForeignManipulationPowerDraw > 0f)
            args.ActivePowerEntries.Add(new NeuroInterfaceMetricEntry("neuro-interface-tooltip-source-power-intercept-penalty", ent.Comp.ForeignManipulationPowerDraw));

        if (ent.Comp.ForeignManipulationNeuroLoad > 0f)
            args.ActiveNeuroLoadEntries.Add(new NeuroInterfaceMetricEntry("neuro-interface-tooltip-source-neuro-intercept-penalty", ent.Comp.ForeignManipulationNeuroLoad));

        if (ent.Comp.ForeignManipulationDurationSeconds > 0f)
            args.ActiveNeuroLoadEntries.Add(new NeuroInterfaceMetricEntry("neuro-interface-tooltip-source-intercept-penalty-duration", ent.Comp.ForeignManipulationDurationSeconds));
    }
}
