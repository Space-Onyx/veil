using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Robust.Shared.Prototypes;
using System;
using System.Linq;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class PresetAugmentsSystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _body = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PresetAugmentsComponent, MapInitEvent>(OnMapInit, after: [typeof(SharedBodySystem)]);
    }

    private void OnMapInit(Entity<PresetAugmentsComponent> ent, ref MapInitEvent args)
    {
        if (!HasComp<BodyComponent>(ent))
            return;

        var bodyParts = _body.GetBodyChildren(ent).ToList();
        if (bodyParts.Count == 0)
            return;

        foreach (var entry in ent.Comp.Entries)
        {
            InstallExplicitEntry(ent, bodyParts, entry);
        }

        foreach (var augmentProto in ent.Comp.Augments)
        {
            var augment = Spawn(augmentProto, Transform(ent).Coordinates);

            if (!TryComp<OrganComponent>(augment, out var organ))
            {
                Log.Warning($"Preset augment '{augmentProto}' on {ToPrettyString(ent)} is not an organ, deleting.");
                QueueDel(augment);
                continue;
            }

            if (!TryInstallAugment(augment, organ, bodyParts))
            {
                Log.Warning(
                    $"Could not install preset augment '{augmentProto}' (slot '{organ.SlotId}') on {ToPrettyString(ent)}.");
                QueueDel(augment);
            }
        }

        RemComp<PresetAugmentsComponent>(ent);
    }

    private void InstallExplicitEntry(
        Entity<PresetAugmentsComponent> ent,
        List<(EntityUid Id, BodyPartComponent Component)> bodyParts,
        PresetAugmentEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Slot))
        {
            Log.Warning($"Preset augment entry on {ToPrettyString(ent)} has empty slot.");
            return;
        }

        var augment = Spawn(entry.Prototype, Transform(ent).Coordinates);
        if (!TryComp<OrganComponent>(augment, out var organ))
        {
            Log.Warning($"Preset augment '{entry.Prototype}' on {ToPrettyString(ent)} is not an organ, deleting.");
            QueueDel(augment);
            return;
        }

        if (!string.Equals(organ.SlotId, entry.Slot, StringComparison.Ordinal))
        {
            Log.Warning(
                $"Preset augment '{entry.Prototype}' slot mismatch on {ToPrettyString(ent)}: organ slot '{organ.SlotId}', configured '{entry.Slot}'.");
            QueueDel(augment);
            return;
        }

        if (!TryInstallExplicit(augment, organ, bodyParts, entry))
        {
            Log.Warning(
                $"Could not install preset augment '{entry.Prototype}' into slot '{entry.Slot}' on {ToPrettyString(ent)}.");
            QueueDel(augment);
        }
    }

    private bool TryInstallAugment(EntityUid augment, OrganComponent organ, List<(EntityUid Id, BodyPartComponent Component)> parts)
    {
        var slotId = organ.SlotId;

        if (TryInstallByPreferredPart(augment, organ, parts, slotId))
            return true;

        foreach (var (partUid, partComp) in parts)
        {
            _body.TryCreateOrganSlot(partUid, slotId, out _, partComp);
            if (!_body.CanInsertOrgan(partUid, slotId, partComp))
                continue;

            if (_body.InsertOrgan(partUid, augment, slotId, partComp, organ))
                return true;
        }

        return false;
    }

    private bool TryInstallByPreferredPart(
        EntityUid augment,
        OrganComponent organ,
        List<(EntityUid Id, BodyPartComponent Component)> parts,
        string slotId)
    {
        if (!TryGetPreferredPartType(slotId, out var partType))
            return false;

        foreach (var (partUid, partComp) in parts)
        {
            if (partComp.PartType != partType)
                continue;

            _body.TryCreateOrganSlot(partUid, slotId, out _, partComp);
            if (!_body.CanInsertOrgan(partUid, slotId, partComp))
                continue;

            if (_body.InsertOrgan(partUid, augment, slotId, partComp, organ))
                return true;
        }

        return false;
    }

    private bool TryInstallExplicit(
        EntityUid augment,
        OrganComponent organ,
        List<(EntityUid Id, BodyPartComponent Component)> parts,
        PresetAugmentEntry entry)
    {
        foreach (var (partUid, partComp) in parts)
        {
            if (entry.PartType != BodyPartType.Other && partComp.PartType != entry.PartType)
                continue;

            if (entry.Symmetry != BodyPartSymmetry.None && partComp.Symmetry != entry.Symmetry)
                continue;

            _body.TryCreateOrganSlot(partUid, entry.Slot, out _, partComp);
            if (!_body.CanInsertOrgan(partUid, entry.Slot, partComp))
                continue;

            if (_body.InsertOrgan(partUid, augment, entry.Slot, partComp, organ))
                return true;
        }

        return false;
    }

    private static bool TryGetPreferredPartType(string slotId, out BodyPartType partType)
    {
        partType = slotId switch
        {
            "headImplant" => BodyPartType.Head,
            "neckImplant" => BodyPartType.Head,
            "brainImplant" => BodyPartType.Head,
            "eyeImplant" => BodyPartType.Head,
            "chestImplant" => BodyPartType.Chest,
            "armImplant" => BodyPartType.Arm,
            "handImplant" => BodyPartType.Hand,
            "legImplant" => BodyPartType.Leg,
            "footImplant" => BodyPartType.Foot,
            _ => BodyPartType.Other,
        };

        return partType != BodyPartType.Other;
    }
}
