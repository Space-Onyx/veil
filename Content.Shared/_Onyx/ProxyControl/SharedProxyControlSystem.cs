using Robust.Shared.Network;

namespace Content.Shared._Onyx.ProxyControl;

public sealed class SharedProxyControlSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;

    public EntityUid ForCamera(EntityUid actor, bool requireAll = false)
    {
        return GetEffectiveActor(actor, ProxyControlRelayFlags.Camera, requireAll);
    }

    public EntityUid ForMovement(EntityUid actor, bool requireAll = false)
    {
        return GetEffectiveActor(actor, ProxyControlRelayFlags.Movement, requireAll);
    }

    public EntityUid ForInteraction(EntityUid actor, bool requireAll = false)
    {
        return GetEffectiveActor(actor, ProxyControlRelayFlags.Interaction, requireAll);
    }

    public EntityUid ForHands(EntityUid actor, bool requireAll = false)
    {
        return GetEffectiveActor(actor, ProxyControlRelayFlags.Hands, requireAll);
    }

    public EntityUid ForInventory(EntityUid actor, bool requireAll = false)
    {
        return GetEffectiveActor(actor, ProxyControlRelayFlags.Inventory, requireAll);
    }

    public EntityUid ForActions(EntityUid actor, bool requireAll = false)
    {
        return GetEffectiveActor(actor, ProxyControlRelayFlags.Actions, requireAll);
    }

    public EntityUid ForSpeech(EntityUid actor, bool requireAll = false)
    {
        return GetEffectiveActor(actor, ProxyControlRelayFlags.Speech, requireAll);
    }

    public EntityUid ForUserInterface(EntityUid actor, bool requireAll = false)
    {
        return GetEffectiveActor(actor, ProxyControlRelayFlags.UserInterface, requireAll);
    }

    public EntityUid ForCombat(EntityUid actor, bool requireAll = false)
    {
        return GetEffectiveActor(actor, ProxyControlRelayFlags.Actions | ProxyControlRelayFlags.Hands, requireAll);
    }

    public EntityUid ForMouseRotation(EntityUid actor, bool requireAll = false)
    {
        return GetEffectiveActor(actor, ProxyControlRelayFlags.Movement | ProxyControlRelayFlags.Hands, requireAll);
    }

    public EntityUid? ForMouseRotationOrNull(EntityUid? actor, bool requireAll = false)
    {
        return GetEffectiveActorOrNull(actor, ProxyControlRelayFlags.Movement | ProxyControlRelayFlags.Hands, requireAll);
    }

    public EntityUid? ForUserInterfaceOrNull(EntityUid? actor, bool requireAll = false)
    {
        return GetEffectiveActorOrNull(actor, ProxyControlRelayFlags.UserInterface, requireAll);
    }

    public EntityUid ForPredictedAudio(EntityUid actor, ProxyControlRelayFlags flags, bool requireAll = false)
    {
        if (TryGetEffectiveActor(actor, flags, out _, requireAll))
            return actor;

        return TryGetControllerForTarget(actor, flags, out var controller, requireAll)
            ? controller
            : actor;
    }

    public bool TryGetControllerForTarget(
        EntityUid target,
        ProxyControlRelayFlags flags,
        out EntityUid controller,
        bool requireAll = false)
    {
        controller = default;

        if (!TryComp<ProxyControlTargetComponent>(target, out var targetComp))
            return false;

        foreach (var candidate in targetComp.Controllers)
        {
            if (!IsControllerFor(candidate, target, flags, requireAll))
                continue;

            controller = candidate;
            return true;
        }

        return false;
    }

    public EntityUid GetEffectiveActor(EntityUid actor, ProxyControlRelayFlags flags, bool requireAll = false)
    {
        return TryGetEffectiveActor(actor, flags, out var target, requireAll) ? target : actor;
    }

    public EntityUid? GetEffectiveActorOrNull(EntityUid? actor, ProxyControlRelayFlags flags, bool requireAll = false)
    {
        if (actor is not { } uid)
            return null;

        return GetEffectiveActor(uid, flags, requireAll);
    }

    public bool TryGetEffectiveActor(EntityUid actor, ProxyControlRelayFlags flags, out EntityUid target, bool requireAll = false)
    {
        target = default;

        return TryComp<ProxyControlComponent>(actor, out var proxy) &&
               TryGetTarget((actor, proxy), flags, out target, requireAll);
    }

    public bool IsControllerFor(EntityUid controller, EntityUid target, ProxyControlRelayFlags flags, bool requireAll = false)
    {
        return TryGetEffectiveActor(controller, flags, out var proxyTarget, requireAll) && proxyTarget == target;
    }

    public bool TryGetTarget(
        Entity<ProxyControlComponent> controller,
        ProxyControlRelayFlags flags,
        out EntityUid target,
        bool requireAll = false)
    {
        target = default;

        if (controller.Comp.Target is not { Valid: true } candidate ||
            TerminatingOrDeleted(candidate) ||
            !HasRequiredRelay(controller.Comp, flags, requireAll))
        {
            return false;
        }

        if (_net.IsServer &&
            (!TryComp<ProxyControlTargetComponent>(candidate, out var targetComp) ||
             !targetComp.Controllers.Contains(controller.Owner)))
        {
            return false;
        }

        target = candidate;
        return true;
    }

    public static bool HasRequiredRelay(ProxyControlComponent proxy, ProxyControlRelayFlags flags, bool requireAll = false)
    {
        if (flags == ProxyControlRelayFlags.None)
            return true;

        var active = GetActiveRelays(proxy);
        return requireAll
            ? (active & flags) == flags
            : (active & flags) != 0;
    }

    public static ProxyControlRelayFlags GetActiveRelays(ProxyControlComponent proxy)
    {
        var flags = ProxyControlRelayFlags.None;

        if (proxy.RelayCamera)
            flags |= ProxyControlRelayFlags.Camera;

        if (proxy.RelayMovement)
            flags |= ProxyControlRelayFlags.Movement;

        if (proxy.RelayInteraction)
            flags |= ProxyControlRelayFlags.Interaction;

        if (proxy.RelayHands)
            flags |= ProxyControlRelayFlags.Hands;

        if (proxy.RelayInventory)
            flags |= ProxyControlRelayFlags.Inventory;

        if (proxy.RelayActions)
            flags |= ProxyControlRelayFlags.Actions;

        if (proxy.RelaySpeech)
            flags |= ProxyControlRelayFlags.Speech;

        return flags;
    }
}
