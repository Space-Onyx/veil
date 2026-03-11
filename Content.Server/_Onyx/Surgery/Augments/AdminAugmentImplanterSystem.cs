using System.Collections.Generic;
using System;
using System.Linq;
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

        var bodyParts = _body.GetBodyChildren(target).ToList();
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

        var part = FindPart(bodyParts, entry);
        if (part == null)
            return false;

        var partValue = part.Value;
        _body.TryCreateOrganSlot(partValue.Id, entry.Slot, out _, partValue.Component);

        EntityUid? existing = null;
        foreach (var organ in _body.GetPartOrgans(partValue.Id))
        {
            if (!string.Equals(organ.Component.SlotId, entry.Slot, StringComparison.Ordinal))
                continue;

            existing = organ.Id;
            break;
        }

        if (existing != null)
        {
            if (!replaceExisting)
                return false;

            _body.RemoveOrgan(existing.Value);
        }

        var augment = Spawn(entry.Prototype, Transform(target).Coordinates);
        if (!TryComp<OrganComponent>(augment, out var organComp))
        {
            QueueDel(augment);
            return false;
        }

        if (!string.Equals(organComp.SlotId, entry.Slot, StringComparison.Ordinal))
        {
            QueueDel(augment);
            return false;
        }

        if (!_body.CanInsertOrgan(partValue.Id, entry.Slot, partValue.Component))
        {
            QueueDel(augment);
            return false;
        }

        if (_body.InsertOrgan(partValue.Id, augment, entry.Slot, partValue.Component, organComp))
            return true;

        QueueDel(augment);
        return false;
    }

    private static (EntityUid Id, BodyPartComponent Component)? FindPart(
        List<(EntityUid Id, BodyPartComponent Component)> bodyParts,
        PresetAugmentEntry entry)
    {
        foreach (var part in bodyParts)
        {
            if (entry.PartType != BodyPartType.Other && part.Component.PartType != entry.PartType)
                continue;

            if (entry.Symmetry != BodyPartSymmetry.None && part.Component.Symmetry != entry.Symmetry)
                continue;

            return part;
        }

        return null;
    }

    private void PopupUser(EntityUid user, string key, params (string, object)[] args)
    {
        var msg = Loc.GetString(key, args);
        _popup.PopupEntity(msg, user, user, PopupType.Medium);
    }
}
