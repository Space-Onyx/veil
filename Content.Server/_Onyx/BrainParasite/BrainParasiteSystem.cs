using Content.Server._Onyx.ProxyControl.Systems;
using Content.Server.Actions;
using Content.Server.Body.Systems;
using Content.Server.Popups;
using Content.Shared._Onyx.BrainParasite;
using Content.Shared._Onyx.ProxyControl;
using Content.Shared.Body.Events;
using Content.Shared.Body.Part;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._Onyx.BrainParasite;

public sealed class BrainParasiteSystem : EntitySystem
{
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly ProxyControlSystem _proxy = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BrainParasiteComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<BrainParasiteComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<BrainParasiteComponent, BrainParasiteEnterHostActionEvent>(OnEnterHostAction);
        SubscribeLocalEvent<BrainParasiteComponent, OrganAddedToBodyEvent>(OnOrganAddedToBody);
        SubscribeLocalEvent<BrainParasiteComponent, OrganRemovedFromBodyEvent>(OnOrganRemovedFromBody);
    }

    private void OnMapInit(EntityUid uid, BrainParasiteComponent component, MapInitEvent args)
    {
        _actions.AddAction(uid, ref component.EnterHostActionEntity, component.EnterHostAction);
    }

    private void OnShutdown(EntityUid uid, BrainParasiteComponent component, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, component.EnterHostActionEntity);
        StopControl(uid, component, "brain parasite shutdown");
    }

    private void OnEnterHostAction(EntityUid uid, BrainParasiteComponent component, BrainParasiteEnterHostActionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryEnterHost(uid, args.Target, component))
            return;

        args.Handled = true;
    }

    private void OnOrganAddedToBody(EntityUid uid, BrainParasiteComponent component, ref OrganAddedToBodyEvent args)
    {
        TryBeginControl(uid, args.Body, component);
    }

    private void OnOrganRemovedFromBody(EntityUid uid, BrainParasiteComponent component, ref OrganRemovedFromBodyEvent args)
    {
        StopControl(uid, component, "brain parasite removed");
    }

    private bool TryEnterHost(EntityUid uid, EntityUid target, BrainParasiteComponent component)
    {
        if (target == uid || component.Host != null)
            return false;

        if (!_mobState.IsAlive(uid) || !_mobState.IsAlive(target))
        {
            _popup.PopupEntity(Loc.GetString("brain-parasite-enter-host-invalid"), uid, uid);
            return false;
        }

        if (!HasComp<ActorComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("brain-parasite-enter-host-not-player"), uid, uid);
            return false;
        }

        if (!_interaction.InRangeUnobstructed(uid, target, popup: true))
            return false;

        var head = _body.GetBodyChildrenOfType(target, BodyPartType.Head, symmetry: BodyPartSymmetry.None).FirstOrNull();
        if (head == null)
        {
            _popup.PopupEntity(Loc.GetString("brain-parasite-enter-host-no-head"), uid, uid);
            return false;
        }

        _body.TryCreateOrganSlot(head.Value.Id, component.OrganSlot, out _, head.Value.Component);

        if (!_body.InsertOrgan(head.Value.Id, uid, component.OrganSlot, head.Value.Component))
        {
            _popup.PopupEntity(Loc.GetString("brain-parasite-enter-host-occupied"), uid, uid);
            return false;
        }

        if (!TryBeginControl(uid, target, component))
        {
            _body.RemoveOrgan(uid);
            return false;
        }

        _popup.PopupEntity(Loc.GetString("brain-parasite-enter-host-success"), target, uid);
        _popup.PopupEntity(Loc.GetString("brain-parasite-enter-host-target"), target, target);
        return true;
    }

    private bool TryBeginControl(EntityUid uid, EntityUid target, BrainParasiteComponent component)
    {
        if (component.Host == target &&
            TryComp<ProxyControlComponent>(uid, out var existingProxy) &&
            existingProxy.Target == target)
        {
            return true;
        }

        if (!HasComp<ActorComponent>(target))
            return false;

        EnsureComp<ProxyControlSourceComponent>(uid);
        EnsureComp<ProxyControlPersistentTargetComponent>(uid);

        if (!_proxy.Link(
                uid,
                target,
                relayCamera: true,
                relayMovement: true,
                relayInteraction: true,
                relayHands: true,
                relayInventory: true,
                relayActions: true,
                relaySpeech: true))
        {
            return false;
        }

        component.Host = target;
        return true;
    }

    private void StopControl(EntityUid uid, BrainParasiteComponent component, string reason)
    {
        if (TryComp<ProxyControlComponent>(uid, out var proxy))
            _proxy.Unlink((uid, proxy), reason);

        component.Host = null;
    }
}
