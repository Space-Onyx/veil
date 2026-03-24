using System;
using Content.Goobstation.Shared.Augments;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Body.Events;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Containers;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentModuleSlotsSystem : EntitySystem
{
    [Dependency] private readonly AugmentSystem _augment = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private static readonly VerbCategory AugmentationsCategory =
        new("augment-modules-verb-category", "/Textures/Interface/VerbIcons/group.svg.192dpi.png");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentModuleSlotsComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<AugmentModuleSlotsComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<AugmentModuleSlotsComponent, OrganAddedToBodyEvent>(OnOrganAdded);
        SubscribeLocalEvent<AugmentModuleSlotsComponent, OrganRemovedFromBodyEvent>(OnOrganRemoved);

        SubscribeLocalEvent<AugmentModuleSlotsComponent, ItemSlotInsertAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<AugmentModuleSlotsComponent, ItemSlotEjectAttemptEvent>(OnEjectAttempt);

        SubscribeLocalEvent<AugmentModuleSlotsComponent, EntInsertedIntoContainerMessage>(OnInsertedIntoContainer);
        SubscribeLocalEvent<AugmentModuleSlotsComponent, EntRemovedFromContainerMessage>(OnRemovedFromContainer);

        SubscribeLocalEvent<GetVerbsEvent<AlternativeVerb>>(OnAnyAlternativeVerbs);
        SubscribeLocalEvent<AugmentModuleSlotsComponent, GetVerbsEvent<AlternativeVerb>>(OnAugmentVerbs);
    }

    private void OnAnyAlternativeVerbs(GetVerbsEvent<AlternativeVerb> args)
    {
        if (!TryComp<InstalledAugmentsComponent>(args.Target, out var installed))
            return;

        OnBodyVerbs((args.Target, installed), ref args);
    }

    private void OnInit(Entity<AugmentModuleSlotsComponent> ent, ref ComponentInit args)
    {
        foreach (var def in ent.Comp.Slots)
        {
            if (string.IsNullOrWhiteSpace(def.Id))
                continue;

            var slot = new ItemSlot
            {
                Name = def.Name,
                Whitelist = def.Whitelist,
                InsertOnInteract = false,
                DisableInsert = true,
                InsertDelay = null,
                EjectDelay = null,
            };

            _itemSlots.AddItemSlot(ent, def.Id, slot);

            if (_itemSlots.TryGetSlot(ent, def.Id, out var addedSlot))
            {
                addedSlot.InsertDelay = null;
                addedSlot.EjectDelay = null;
            }
        }
    }

    private void OnShutdown(Entity<AugmentModuleSlotsComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.PanelOpen = false;
    }

    private void OnOrganAdded(Entity<AugmentModuleSlotsComponent> ent, ref OrganAddedToBodyEvent args)
    {
        SetPanelState(ent, args.Body, false, silent: true);
    }

    private void OnOrganRemoved(Entity<AugmentModuleSlotsComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        SetPanelState(ent, null, false, silent: true);
    }

    private void OnInsertAttempt(Entity<AugmentModuleSlotsComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        var slotId = args.Slot.ID;
        if (args.Cancelled || slotId == null || !TryGetDefinition(ent.Comp, slotId, out var def))
            return;

        var body = _augment.GetBody(ent);
        if (body == null)
        {
            if (!def.AllowInsertWhenUninstalled)
                args.Cancelled = true;
        }
        else
        {
            if (!def.AllowInsertWhenInstalled || !ent.Comp.PanelOpen)
                args.Cancelled = true;
        }

        if (args.Cancelled)
            return;

        var ev = new AugmentModuleInsertAttemptEvent(ent.Owner, slotId, args.Item, args.User, body);
        RaiseLocalEvent(ent, ref ev);
        args.Cancelled = ev.Cancelled;
    }

    private void OnEjectAttempt(Entity<AugmentModuleSlotsComponent> ent, ref ItemSlotEjectAttemptEvent args)
    {
        var slotId = args.Slot.ID;
        if (args.Cancelled || slotId == null || !TryGetDefinition(ent.Comp, slotId, out _))
            return;

        var body = _augment.GetBody(ent);
        if (body != null && !ent.Comp.PanelOpen)
        {
            args.Cancelled = true;
            return;
        }

        var ev = new AugmentModuleEjectAttemptEvent(ent.Owner, slotId, args.Item, args.User, body);
        RaiseLocalEvent(ent, ref ev);
        args.Cancelled = ev.Cancelled;
    }

    private void OnInsertedIntoContainer(Entity<AugmentModuleSlotsComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (!TryGetDefinition(ent.Comp, args.Container.ID, out _))
            return;

        var body = _augment.GetBody(ent);
        var ev = new AugmentModuleInsertedEvent(ent.Owner, args.Container.ID, args.Entity, body);
        RaiseLocalEvent(ent, ref ev);
    }

    private void OnRemovedFromContainer(Entity<AugmentModuleSlotsComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (!TryGetDefinition(ent.Comp, args.Container.ID, out _))
            return;

        var body = _augment.GetBody(ent);
        var ev = new AugmentModuleRemovedEvent(ent.Owner, args.Container.ID, args.Entity, body);
        RaiseLocalEvent(ent, ref ev);
    }

    private void OnBodyVerbs(Entity<InstalledAugmentsComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var body = ent.Owner;
        var user = args.User;

        foreach (var netAug in ent.Comp.InstalledAugments)
        {
            var augment = GetEntity(netAug);
            if (!TryComp<AugmentModuleSlotsComponent>(augment, out var modules))
                continue;

            if (!HasPostInstallSlots(modules))
                continue;

            var augmentName = Name(augment);

            if (user == body)
                AddPanelToggleVerb(ref args, augment, body, augmentName, modules.PanelOpen);

            if (!modules.PanelOpen)
                continue;

            if (args.Using is { } usingEnt)
                AddInsertVerbs(ref args, augment, modules, augmentName, usingEnt, user, installed: true);

            AddEjectVerbs(ref args, augment, modules, augmentName, user);

            CollectNestedModuleVerbs(ref args, augment, modules, body, user);
        }
    }

    private void CollectNestedModuleVerbs(
        ref GetVerbsEvent<AlternativeVerb> args,
        EntityUid parentAugment,
        AugmentModuleSlotsComponent parentModules,
        EntityUid body,
        EntityUid user,
        int depth = 0)
    {
        if (depth > 4)
            return;

        foreach (var def in parentModules.Slots)
        {
            if (!_itemSlots.TryGetSlot(parentAugment, def.Id, out var slot) || slot.Item is not { } moduleEntity)
                continue;

            if (!TryComp<AugmentModuleSlotsComponent>(moduleEntity, out var nestedModules))
                continue;

            if (!HasPostInstallSlots(nestedModules))
                continue;

            var moduleName = Name(moduleEntity);

            if (user == body)
                AddPanelToggleVerb(ref args, moduleEntity, body, moduleName, nestedModules.PanelOpen);

            if (!nestedModules.PanelOpen)
                continue;

            if (args.Using is { } usingEnt)
                AddInsertVerbs(ref args, moduleEntity, nestedModules, moduleName, usingEnt, user, installed: true);

            AddEjectVerbs(ref args, moduleEntity, nestedModules, moduleName, user);

            CollectNestedModuleVerbs(ref args, moduleEntity, nestedModules, body, user, depth + 1);
        }
    }

    private void OnAugmentVerbs(Entity<AugmentModuleSlotsComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var body = _augment.GetBody(ent);
        if (body != null)
            return;

        var augmentName = Name(ent);
        var user = args.User;

        if (args.Using is { } usingEnt)
            AddInsertVerbs(ref args, ent.Owner, ent.Comp, augmentName, usingEnt, user, installed: false);

        AddEjectVerbs(ref args, ent.Owner, ent.Comp, augmentName, user);
    }

    private void SetPanelState(Entity<AugmentModuleSlotsComponent> ent, EntityUid? body, bool open, bool silent)
    {
        if (ent.Comp.PanelOpen == open)
            return;

        ent.Comp.PanelOpen = open;
        Dirty(ent, ent.Comp);

        var ev = new AugmentModulePanelStateChangedEvent(body, open);
        RaiseLocalEvent(ent, ref ev);

        if (silent || body == null)
            return;

        var popup = open
            ? "augment-modules-popup-panel-opened"
            : "augment-modules-popup-panel-closed";
        _popup.PopupEntity(Loc.GetString(popup, ("augment", Name(ent))), body.Value, body.Value);
    }

    private static bool TryGetDefinition(AugmentModuleSlotsComponent comp, string slotId, out AugmentModuleSlotDefinition def)
    {
        foreach (var candidate in comp.Slots)
        {
            if (!string.Equals(candidate.Id, slotId, StringComparison.Ordinal))
                continue;

            def = candidate;
            return true;
        }

        def = default!;
        return false;
    }

    private static bool HasPostInstallSlots(AugmentModuleSlotsComponent comp)
    {
        foreach (var def in comp.Slots)
        {
            if (def.AllowInsertWhenInstalled)
                return true;
        }

        return false;
    }

    private string GetSlotLabel(AugmentModuleSlotDefinition def)
    {
        return def.Name.StartsWith("augment-", StringComparison.Ordinal)
            ? Loc.GetString(def.Name)
            : def.Name;
    }

    private void TryInsertModuleNoDelay(EntityUid augment, string slotId, EntityUid module, EntityUid user)
    {
        if (_itemSlots.TryGetSlot(augment, slotId, out var slot)
            && TryComp<HandsComponent>(user, out var hands))
        {
            _itemSlots.TryInsertOrDoAfter(augment, (user, hands), module, slot, doAfter: false);
            return;
        }

        _itemSlots.TryInsert(augment, slotId, module, user, excludeUserAudio: true);
    }

    private void AddPanelToggleVerb(
        ref GetVerbsEvent<AlternativeVerb> args,
        EntityUid augment,
        EntityUid body,
        string augmentName,
        bool currentlyOpen)
    {
        var open = !currentlyOpen;
        var textLoc = open
            ? "augment-modules-verb-open-panel"
            : "augment-modules-verb-close-panel";

        args.Verbs.Add(new AlternativeVerb
        {
            Text = $"({augmentName}) {Loc.GetString(textLoc)}",
            Category = AugmentationsCategory,
            Act = () =>
            {
                if (!TryComp<AugmentModuleSlotsComponent>(augment, out var comp))
                    return;

                SetPanelState((augment, comp), body, open, silent: false);
            }
        });
    }

    private void AddInsertVerbs(
        ref GetVerbsEvent<AlternativeVerb> args,
        EntityUid augment,
        AugmentModuleSlotsComponent modules,
        string augmentName,
        EntityUid usingEnt,
        EntityUid user,
        bool installed)
    {
        foreach (var def in modules.Slots)
        {
            if (!def.VisibleInVerbs)
                continue;

            var canInsert = installed ? def.AllowInsertWhenInstalled : def.AllowInsertWhenUninstalled;
            if (!canInsert)
                continue;

            if (!_itemSlots.TryGetSlot(augment, def.Id, out var slot)
                || slot.Item != null
                || !_itemSlots.CanInsert(augment, usingEnt, user, slot))
            {
                continue;
            }

            var slotLabel = GetSlotLabel(def);
            args.Verbs.Add(new AlternativeVerb
            {
                Text = $"({augmentName}) {Loc.GetString("augment-modules-verb-insert-module-short", ("slot", slotLabel))}",
                Category = AugmentationsCategory,
                Act = () => TryInsertModuleNoDelay(augment, def.Id, usingEnt, user),
            });
        }
    }

    private void AddEjectVerbs(
        ref GetVerbsEvent<AlternativeVerb> args,
        EntityUid augment,
        AugmentModuleSlotsComponent modules,
        string augmentName,
        EntityUid user)
    {
        foreach (var def in modules.Slots)
        {
            if (!def.VisibleInVerbs
                || !_itemSlots.TryGetSlot(augment, def.Id, out var slot)
                || slot.Item is not { } item)
            {
                continue;
            }

            args.Verbs.Add(new AlternativeVerb
            {
                Text = $"({augmentName}) {Loc.GetString("augment-modules-verb-eject-module", ("module", Name(item)))}",
                Category = AugmentationsCategory,
                Act = () => _itemSlots.TryEjectToHands(augment, slot, user, excludeUserAudio: true, doAfter: false),
            });
        }
    }
}
