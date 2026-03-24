using Content.Goobstation.Shared.Augments;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Popups;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class CyberDeckScriptSystem : EntitySystem
{
    [Dependency] private readonly AugmentSystem _augment = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly CyberDeckSystem _cyberDeck = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

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

        if (!TryPrepareExecution(ent, out var parent, out var body))
            return;

        if (TryComp<CyberDeckScriptActivatableUIComponent>(ent, out var uiComp) &&
            uiComp.Key is { } key &&
            _ui.HasUi(ent, key))
        {
            _ui.OpenUi(ent.Owner, key, body);
        }

        var executed = new CyberDeckScriptExecutedEvent(body, parent, args.Performer);
        RaiseLocalEvent(ent.Owner, ref executed);

        args.Handled = true;
    }

    private void OnScriptTargetAction(Entity<CyberDeckScriptComponent> ent, ref CyberDeckScriptTargetActionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryPrepareExecution(ent, out var parent, out var body))
            return;

        var executed = new CyberDeckScriptExecutedEvent(
            body,
            parent,
            args.Performer,
            args.Entity,
            args.Target);
        RaiseLocalEvent(ent.Owner, ref executed);

        args.Handled = true;
    }

    private bool TryPrepareExecution(
        Entity<CyberDeckScriptComponent> ent,
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

        if (_cyberDeck.TrySpendRam(cyberDeck, ent.Comp.RamCost, deckComp))
            return true;

        _popup.PopupEntity(
            Loc.GetString("cyberdeck-script-not-enough-ram"),
            body,
            body,
            PopupType.SmallCaution);

        return false;
    }
}
