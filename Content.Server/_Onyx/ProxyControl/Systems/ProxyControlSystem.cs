using Content.Server.Chat.Systems;
using Content.Server.Hands.Systems;
using Content.Shared._Onyx.ProxyControl;
using Content.Shared.Administration.Logs;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.SSDIndicator;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.ProxyControl.Systems;

public sealed class ProxyControlSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly SharedEyeSystem _eye = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedMoverController _mover = default!;
    [Dependency] private readonly SSDIndicatorSystem _ssd = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ProxyControlComponent, ComponentShutdown>(OnControllerShutdown);
        SubscribeLocalEvent<ProxyControlComponent, PlayerDetachedEvent>(OnControllerPlayerDetached);
        SubscribeLocalEvent<ProxyControlComponent, MindRemovedMessage>(OnControllerMindRemoved);
        SubscribeLocalEvent<ProxyControlTargetComponent, ComponentShutdown>(OnTargetShutdown);
        SubscribeLocalEvent<ProxyControlTargetComponent, PlayerDetachedEvent>(OnTargetPlayerDetached);
        SubscribeLocalEvent<ProxyControlTargetComponent, MindRemovedMessage>(OnTargetMindRemoved);
        SubscribeLocalEvent<ProxyControlTargetComponent, MobStateChangedEvent>(OnTargetMobStateChanged);
        SubscribeLocalEvent<ProxyControlComponent, PlayerAttachedEvent>(OnControllerPlayerAttached);
    }

    private void OnControllerShutdown(Entity<ProxyControlComponent> ent, ref ComponentShutdown args)
    {
        Unlink(ent);
    }

    private void OnTargetShutdown(Entity<ProxyControlTargetComponent> ent, ref ComponentShutdown args)
    {
        foreach (var controller in new List<EntityUid>(ent.Comp.Controllers))
        {
            if (TryComp<ProxyControlComponent>(controller, out var proxy))
                Unlink((controller, proxy));
        }

        ent.Comp.Controllers.Clear();
    }

    private void OnControllerPlayerDetached(Entity<ProxyControlComponent> ent, ref PlayerDetachedEvent args)
    {
        Unlink(ent, "controller detached");
    }

    private void OnControllerMindRemoved(Entity<ProxyControlComponent> ent, ref MindRemovedMessage args)
    {
        Unlink(ent, "controller mind removed");
    }

    private void OnTargetPlayerDetached(Entity<ProxyControlTargetComponent> ent, ref PlayerDetachedEvent args)
    {
        UnlinkTargetControllers(ent, "target detached", persistentTargetsStayLinked: true);
    }

    private void OnTargetMindRemoved(Entity<ProxyControlTargetComponent> ent, ref MindRemovedMessage args)
    {
        UnlinkTargetControllers(ent, "target mind removed", persistentTargetsStayLinked: true);
    }

    private void OnTargetMobStateChanged(Entity<ProxyControlTargetComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            UnlinkTargetControllers(ent, "target died");
    }

    private void OnControllerPlayerAttached(Entity<ProxyControlComponent> ent, ref PlayerAttachedEvent args)
    {
        RefreshRelays(ent);
    }

    public bool Link(
        EntityUid controller,
        EntityUid target,
        bool relayCamera = true,
        bool relayMovement = false,
        bool relayInteraction = false,
        bool relayHands = true,
        bool relayInventory = true,
        bool relayActions = true,
        bool relaySpeech = true)
    {
        if (controller == target ||
            TerminatingOrDeleted(controller) ||
            TerminatingOrDeleted(target) ||
            HasComp<ProxyControlImmuneComponent>(target) ||
            HasComp<ProxyControlRequiresSourceComponent>(target) && !HasComp<ProxyControlSourceComponent>(controller))
        {
            return false;
        }

        if (IsActiveProxyTarget(controller) ||
            IsActiveProxyTargetForOtherController(target, controller) ||
            IsActiveProxyController(target) ||
            WouldCreateProxyCycle(controller, target))
        {
            return false;
        }

        var proxy = EnsureComp<ProxyControlComponent>(controller);
        if (proxy.IsLinked)
            Unlink((controller, proxy));

        var targetComp = EnsureComp<ProxyControlTargetComponent>(target);
        targetComp.Controllers.Add(controller);

        proxy.Target = target;
        proxy.RelayCamera = relayCamera;
        proxy.RelayMovement = relayMovement;
        proxy.RelayInteraction = relayInteraction;
        proxy.RelayHands = relayHands;
        proxy.RelayInventory = relayInventory;
        proxy.RelayActions = relayActions;
        proxy.RelaySpeech = relaySpeech;
        proxy.NextProxyAction = TimeSpan.Zero;

        Dirty(controller, proxy);

        _ssd.RefreshProxySsdState(target);

        RefreshRelays((controller, proxy));
        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(controller):controller} linked proxy control to {ToPrettyString(target):target} " +
            $"with relays camera={relayCamera}, movement={relayMovement}, interaction={relayInteraction}, hands={relayHands}, inventory={relayInventory}, actions={relayActions}, speech={relaySpeech}");
        return true;
    }

    public void Unlink(Entity<ProxyControlComponent> controller, string reason = "manual")
    {
        if (controller.Comp.Target is not { } target)
            return;

        ClearRelays(controller);

        if (TryComp<ProxyControlTargetComponent>(target, out var targetComp))
        {
            targetComp.Controllers.Remove(controller.Owner);

            if (targetComp.Controllers.Count == 0)
                RemCompDeferred(target, targetComp);
        }

        controller.Comp.Target = null;
        controller.Comp.NextProxyAction = TimeSpan.Zero;
        Dirty(controller);

        if (!TerminatingOrDeleted(target))
            _ssd.RefreshProxySsdState(target);

        _adminLogger.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(controller.Owner):controller} unlinked proxy control from {ToPrettyString(target):target} ({reason})");
    }

    public bool TryGetTarget(EntityUid controller, out EntityUid target, ProxyControlComponent? proxy = null)
    {
        target = default;

        if (!Resolve(controller, ref proxy, false) ||
            proxy.Target is not { } candidate)
            return false;

        if (TerminatingOrDeleted(candidate) ||
            !TryComp<ProxyControlTargetComponent>(candidate, out var targetComp) ||
            !targetComp.Controllers.Contains(controller))
        {
                Unlink((controller, proxy), "invalid target");
            return false;
        }

        target = candidate;
        return true;
    }

    public bool TryProxySay(EntityUid controller, string message)
    {
        if (!TryConsumeProxyAction(controller, out var target, out var proxy) ||
            !proxy.RelaySpeech)
            return false;

        _chat.TrySendInGameICMessage(target, message, InGameICChatType.Speak, ChatTransmitRange.Normal);
        return true;
    }

    public bool TryProxyUse(EntityUid controller, bool altInteract = false)
    {
        return TryConsumeProxyAction(controller, out var target, out var proxy) &&
               proxy.RelayHands &&
               _hands.TryUseItemInHand(target, altInteract);
    }

    public bool TryProxyDrop(EntityUid controller)
    {
        if (!TryConsumeProxyAction(controller, out var target, out var proxy) ||
            !proxy.RelayHands ||
            !TryComp<HandsComponent>(target, out var hands))
            return false;

        return _hands.TryDrop((target, hands));
    }

    public bool TryProxySwapHands(EntityUid controller, bool reverse)
    {
        if (!TryConsumeProxyAction(controller, out var target, out var proxy) ||
            !proxy.RelayHands ||
            !TryComp<HandsComponent>(target, out var hands))
            return false;

        _hands.SwapHands((target, hands), reverse);
        return true;
    }

    public bool TryProxyPickup(EntityUid controller, EntityUid item)
    {
        if (!TryConsumeProxyAction(controller, out var target, out var proxy) ||
            !proxy.RelayHands)
            return false;

        if (!_interaction.InRangeUnobstructed(target, item, popup: true))
            return false;

        return _hands.TryPickupAnyHand(target, item, animateUser: true);
    }

    public bool TryProxyThrow(EntityUid controller, EntityCoordinates coordinates)
    {
        return TryConsumeProxyAction(controller, out var target, out var proxy) &&
               proxy.RelayHands &&
               _hands.ThrowHeldItem(target, coordinates);
    }

    private bool TryConsumeProxyAction(EntityUid controller, out EntityUid target, out ProxyControlComponent proxy)
    {
        target = default;
        proxy = default!;

        if (!TryComp<ProxyControlComponent>(controller, out var foundProxy) ||
            !TryGetTarget(controller, out target, foundProxy))
            return false;

        proxy = foundProxy;

        if (_timing.CurTime < proxy.NextProxyAction)
            return false;

        proxy.NextProxyAction = _timing.CurTime + proxy.ProxyCooldown;
        Dirty(controller, proxy);
        return true;
    }

    private void RefreshRelays(Entity<ProxyControlComponent> controller)
    {
        if (controller.Comp.Target is not { } target || TerminatingOrDeleted(target))
            return;

        if (controller.Comp.RelayCamera)
            RefreshCameraRelay(controller, target);

        if (controller.Comp.RelayMovement)
            RefreshMovementRelay(controller, target);

        if (controller.Comp.RelayInteraction)
            RefreshInteractionRelay(controller, target);

        Dirty(controller);
    }

    private void RefreshCameraRelay(Entity<ProxyControlComponent> controller, EntityUid target)
    {
        if (!controller.Comp.CapturedEyeState)
        {
            controller.Comp.HadEyeComponent = TryComp<EyeComponent>(controller, out var oldEye);
            controller.Comp.HadPreviousEyeTarget = oldEye?.Target != null;
            controller.Comp.PreviousEyeTarget = oldEye?.Target;
            controller.Comp.CapturedEyeState = true;
        }

        var eye = EnsureComp<EyeComponent>(controller);
        _eye.SetTarget(controller, target, eye);
    }

    private void RefreshMovementRelay(Entity<ProxyControlComponent> controller, EntityUid target)
    {
        if (!controller.Comp.CapturedMovementRelayState)
        {
            controller.Comp.HadMovementRelay = TryComp<RelayInputMoverComponent>(controller, out var oldRelay);
            controller.Comp.PreviousMovementRelay = oldRelay?.RelayEntity ?? EntityUid.Invalid;
            controller.Comp.CapturedMovementRelayState = true;
        }

        _mover.SetRelay(controller, target, relayCanMove: false, relayInputWhileIncapacitated: true);

        if (TryComp<ActorComponent>(controller, out var actor))
            _mover.ApplyClientMovementSettings(target, actor.PlayerSession);
    }

    private void RefreshInteractionRelay(Entity<ProxyControlComponent> controller, EntityUid target)
    {
        if (!controller.Comp.CapturedInteractionRelayState)
        {
            controller.Comp.HadInteractionRelay = TryComp<InteractionRelayComponent>(controller, out var oldRelay);
            controller.Comp.PreviousInteractionRelay = oldRelay?.RelayEntity;
            controller.Comp.CreatedInteractionRelay = !controller.Comp.HadInteractionRelay;
            controller.Comp.CapturedInteractionRelayState = true;
        }

        var relay = EnsureComp<InteractionRelayComponent>(controller);
        _interaction.SetRelay(controller, target, relay);
    }

    private void ClearRelays(Entity<ProxyControlComponent> controller)
    {
        var target = controller.Comp.Target;

        ClearCameraRelay(controller);
        ClearMovementRelay(controller, target);
        ClearInteractionRelay(controller, target);
        ClearCapturedRelayState(controller.Comp);
    }

    private void ClearCameraRelay(Entity<ProxyControlComponent> controller)
    {
        if (!controller.Comp.RelayCamera || !TryComp<EyeComponent>(controller, out var eye))
            return;

        if (!controller.Comp.HadEyeComponent)
        {
            RemCompDeferred(controller, eye);
            return;
        }

        var previous = controller.Comp.HadPreviousEyeTarget ? controller.Comp.PreviousEyeTarget : null;
        _eye.SetTarget(controller, previous, eye);
    }

    private void ClearMovementRelay(Entity<ProxyControlComponent> controller, EntityUid? target)
    {
        if (!controller.Comp.RelayMovement ||
            target == null ||
            !TryComp<RelayInputMoverComponent>(controller, out var relayMover) ||
            relayMover.RelayEntity != target)
            return;

        if (controller.Comp.HadMovementRelay &&
            controller.Comp.PreviousMovementRelay != EntityUid.Invalid &&
            !TerminatingOrDeleted(controller.Comp.PreviousMovementRelay))
        {
            _mover.SetRelay(controller, controller.Comp.PreviousMovementRelay);
            return;
        }

        RemComp<RelayInputMoverComponent>(controller);
    }

    private void ClearInteractionRelay(Entity<ProxyControlComponent> controller, EntityUid? target)
    {
        if (!controller.Comp.RelayInteraction ||
            !TryComp<InteractionRelayComponent>(controller, out var relayInteraction) ||
            relayInteraction.RelayEntity != target)
            return;

        if (controller.Comp.CreatedInteractionRelay)
            RemComp<InteractionRelayComponent>(controller);
        else
            _interaction.SetRelay(controller, controller.Comp.PreviousInteractionRelay, relayInteraction);
    }

    private static void ClearCapturedRelayState(ProxyControlComponent proxy)
    {
        proxy.HadEyeComponent = false;
        proxy.CapturedEyeState = false;
        proxy.HadPreviousEyeTarget = false;
        proxy.PreviousEyeTarget = null;
        proxy.CapturedMovementRelayState = false;
        proxy.HadMovementRelay = false;
        proxy.PreviousMovementRelay = EntityUid.Invalid;
        proxy.CapturedInteractionRelayState = false;
        proxy.HadInteractionRelay = false;
        proxy.PreviousInteractionRelay = null;
        proxy.CreatedInteractionRelay = false;
    }

    private void UnlinkTargetControllers(Entity<ProxyControlTargetComponent> target, string reason, bool persistentTargetsStayLinked = false)
    {
        foreach (var controller in new List<EntityUid>(target.Comp.Controllers))
        {
            if (persistentTargetsStayLinked && HasComp<ProxyControlPersistentTargetComponent>(controller))
                continue;

            if (TryComp<ProxyControlComponent>(controller, out var proxy))
                Unlink((controller, proxy), reason);
        }
    }

    private bool IsActiveProxyTarget(EntityUid uid)
    {
        return TryComp<ProxyControlTargetComponent>(uid, out var target) &&
               HasValidControllers((uid, target));
    }

    private bool IsActiveProxyTargetForOtherController(EntityUid targetUid, EntityUid controller)
    {
        if (!TryComp<ProxyControlTargetComponent>(targetUid, out var target))
            return false;

        foreach (var existingController in new List<EntityUid>(target.Controllers))
        {
            if (TerminatingOrDeleted(existingController) ||
                !TryComp<ProxyControlComponent>(existingController, out var proxy) ||
                proxy.Target != targetUid)
            {
                target.Controllers.Remove(existingController);
                continue;
            }

            if (existingController != controller)
                return true;
        }

        return false;
    }

    private bool HasValidControllers(Entity<ProxyControlTargetComponent> target)
    {
        var hasValidController = false;
        foreach (var controller in new List<EntityUid>(target.Comp.Controllers))
        {
            if (TerminatingOrDeleted(controller) ||
                !TryComp<ProxyControlComponent>(controller, out var proxy) ||
                proxy.Target != target.Owner)
            {
                target.Comp.Controllers.Remove(controller);
                continue;
            }

            hasValidController = true;
        }

        return hasValidController;
    }

    private bool IsActiveProxyController(EntityUid uid)
    {
        return TryComp<ProxyControlComponent>(uid, out var proxy) && proxy.Target is { Valid: true };
    }

    private bool WouldCreateProxyCycle(EntityUid controller, EntityUid target)
    {
        var current = target;
        var seen = new HashSet<EntityUid> { controller };

        while (TryComp<ProxyControlComponent>(current, out var proxy) &&
               proxy.Target is { Valid: true } next)
        {
            if (!seen.Add(current) || next == controller)
                return true;

            current = next;
        }

        return false;
    }
}
