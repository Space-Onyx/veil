using System;
using Content.Goobstation.Shared.Augments;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Verbs;

namespace Content.Client._Onyx.Surgery.Augments;

public sealed class AugmentModuleSlotsPredictionSystem : EntitySystem
{
    [Dependency] private readonly AugmentSystem _augment = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    private static readonly VerbCategory AugmentationsCategory =
        new("augment-modules-verb-category", "/Textures/Interface/VerbIcons/group.svg.192dpi.png");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GetVerbsEvent<AlternativeVerb>>(OnAnyAlternativeVerbs);
        SubscribeLocalEvent<AugmentModuleSlotsComponent, GetVerbsEvent<AlternativeVerb>>(OnAugmentVerbs);
    }

    private void OnAnyAlternativeVerbs(GetVerbsEvent<AlternativeVerb> args)
    {
        if (!TryComp<InstalledAugmentsComponent>(args.Target, out var installed))
            return;

        OnBodyVerbs((args.Target, installed), ref args);
    }

    private void OnBodyVerbs(Entity<InstalledAugmentsComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var user = args.User;

        foreach (var netAug in ent.Comp.InstalledAugments)
        {
            var augment = GetEntity(netAug);
            if (!TryComp<AugmentModuleSlotsComponent>(augment, out var modules))
                continue;

            if (!modules.PanelOpen)
                continue;

            if (args.Using is { } usingEnt)
                AddInsertVerbs(ref args, augment, modules, Name(augment), usingEnt, user, installed: true);

            AddEjectVerbs(ref args, augment, modules, Name(augment), user);
        }
    }

    private void OnAugmentVerbs(Entity<AugmentModuleSlotsComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        if (_augment.GetBody(ent) != null)
            return;

        var user = args.User;

        if (args.Using is { } usingEnt)
            AddInsertVerbs(ref args, ent.Owner, ent.Comp, Name(ent), usingEnt, user, installed: false);

        AddEjectVerbs(ref args, ent.Owner, ent.Comp, Name(ent), user);
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

        _itemSlots.TryInsert(augment, slotId, module, user);
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
                Text = Loc.GetString("augment-modules-verb-insert-module-short",
                    ("slot", slotLabel)),
                Message = Loc.GetString("augment-modules-verb-insert-module",
                    ("augment", augmentName),
                    ("slot", slotLabel)),
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

            var slotLabel = GetSlotLabel(def);
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("augment-modules-verb-eject-module",
                    ("augment", augmentName),
                    ("slot", slotLabel),
                    ("module", Name(item))),
                Category = AugmentationsCategory,
                Act = () => _itemSlots.TryEjectToHands(augment, slot, user, doAfter: false),
            });
        }
    }
}
