using System.Collections.Generic;
using System;
using Content.Server.Popups;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Interaction;
using Content.Shared.Popups;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AdminAugmentImplanterSystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AdminAugmentImplanterComponent, AfterInteractEvent>(OnAfterInteract);
    }

    private void OnAfterInteract(Entity<AdminAugmentImplanterComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target == null || !args.CanReach)
            return;

        if (ent.Comp.OneTimeUse && ent.Comp.Used)
        {
            PopupUser(args.User, "admin-augment-implanter-popup-used");
            args.Handled = true;
            return;
        }

        if (ent.Comp.Entries.Count == 0)
        {
            PopupUser(args.User, "admin-augment-implanter-popup-no-entries");
            args.Handled = true;
            return;
        }

        var target = args.Target.Value;
        if (!HasComp<BodyComponent>(target))
        {
            PopupUser(args.User, "admin-augment-implanter-popup-no-body");
            args.Handled = true;
            return;
        }

        var bodyParts = AugmentBodyPartHelpers.CollectBodyParts(target, _body);
        if (bodyParts.Count == 0)
        {
            PopupUser(args.User, "admin-augment-implanter-popup-no-body");
            args.Handled = true;
            return;
        }

        var installed = 0;
        foreach (var entry in ent.Comp.Entries)
        {
            if (TryInstallEntry(target, bodyParts, entry, ent.Comp.ReplaceExisting))
                installed++;
        }

        var failed = ent.Comp.Entries.Count - installed;
        var msgKey = failed == 0
            ? "admin-augment-implanter-popup-success"
            : "admin-augment-implanter-popup-partial";
        PopupUser(args.User, msgKey, ("installed", installed), ("failed", failed));

        if (installed > 0 && ent.Comp.OneTimeUse)
        {
            ent.Comp.Used = true;
            Dirty(ent);
        }

        args.Handled = true;
    }

    private bool TryInstallEntry(
        EntityUid target,
        List<(EntityUid Id, BodyPartComponent Component)> bodyParts,
        PresetAugmentEntry entry,
        bool replaceExisting)
    {
        if (string.IsNullOrWhiteSpace(entry.Slot))
            return false;

        if (!TryFindPart(bodyParts, entry, out var part))
            return false;

        _body.TryCreateOrganSlot(part.Id, entry.Slot, out _, part.Component);

        if (TryGetPartOrganInSlot(part.Id, entry.Slot, out var existing))
        {
            if (!replaceExisting)
                return false;

            _body.RemoveOrgan(existing);
        }

        if (!TrySpawnMatchingOrgan(target, entry, out var augment, out var organComp))
            return false;

        if (!_body.CanInsertOrgan(part.Id, entry.Slot, part.Component))
        {
            QueueDel(augment);
            return false;
        }

        if (_body.InsertOrgan(part.Id, augment, entry.Slot, part.Component, organComp))
            return true;

        QueueDel(augment);
        return false;
    }

    private static bool TryFindPart(
        List<(EntityUid Id, BodyPartComponent Component)> bodyParts,
        PresetAugmentEntry entry,
        out (EntityUid Id, BodyPartComponent Component) part)
    {
        foreach (var candidate in bodyParts)
        {
            if (entry.PartType != BodyPartType.Other && candidate.Component.PartType != entry.PartType)
                continue;

            if (entry.Symmetry != BodyPartSymmetry.None && candidate.Component.Symmetry != entry.Symmetry)
                continue;

            part = candidate;
            return true;
        }

        part = default;
        return false;
    }

    private bool TryGetPartOrganInSlot(EntityUid partUid, string slotId, out EntityUid organUid)
    {
        foreach (var organ in _body.GetPartOrgans(partUid))
        {
            if (!string.Equals(organ.Component.SlotId, slotId, StringComparison.Ordinal))
                continue;

            organUid = organ.Id;
            return true;
        }

        organUid = default;
        return false;
    }

    private bool TrySpawnMatchingOrgan(
        EntityUid target,
        PresetAugmentEntry entry,
        out EntityUid augment,
        out OrganComponent organComp)
    {
        augment = Spawn(entry.Prototype, Transform(target).Coordinates);
        if (!TryComp<OrganComponent>(augment, out var comp) || comp == null)
        {
            QueueDel(augment);
            organComp = default!;
            return false;
        }

        organComp = comp;
        if (string.Equals(organComp.SlotId, entry.Slot, StringComparison.Ordinal))
            return true;

        QueueDel(augment);
        return false;
    }

    private void PopupUser(EntityUid user, string key, params (string, object)[] args)
    {
        var msg = Loc.GetString(key, args);
        _popup.PopupEntity(msg, user, user, PopupType.Medium);
    }
}
