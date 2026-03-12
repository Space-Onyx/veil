using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class PresetAugmentsSystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _body = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PresetAugmentsComponent, MapInitEvent>(OnMapInit, after: [typeof(SharedBodySystem)]);
        SubscribeLocalEvent<JobPresetAugmentsComponent, ComponentStartup>(OnJobPresetStartup);
        SubscribeLocalEvent<BodyComponent, ComponentStartup>(OnBodyStartup);
    }

    private void OnMapInit(Entity<PresetAugmentsComponent> ent, ref MapInitEvent args)
    {
        if (!TryApplyPresetAugments(ent.Owner, ent.Comp.Entries, ent.Comp.Augments))
            return;

        RemComp<PresetAugmentsComponent>(ent);
    }

    private void OnJobPresetStartup(Entity<JobPresetAugmentsComponent> ent, ref ComponentStartup args)
    {
        if (!TryApplyPresetAugments(ent.Owner, ent.Comp.Entries, ent.Comp.Augments))
            return;

        RemComp<JobPresetAugmentsComponent>(ent);
    }

    private void OnBodyStartup(Entity<BodyComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<JobPresetAugmentsComponent>(ent, out var presetComp))
            return;

        if (!TryApplyPresetAugments(ent.Owner, presetComp.Entries, presetComp.Augments))
            return;

        RemComp<JobPresetAugmentsComponent>(ent);
    }

    private bool TryApplyPresetAugments(
        EntityUid owner,
        List<PresetAugmentEntry> entries,
        List<EntProtoId> augments)
    {
        if (!HasComp<BodyComponent>(owner))
            return false;

        var bodyParts = AugmentBodyPartHelpers.CollectBodyParts(owner, _body);
        if (bodyParts.Count == 0)
            return false;

        foreach (var entry in entries)
        {
            InstallExplicitEntry(owner, bodyParts, entry);
        }

        foreach (var augmentProto in augments)
        {
            if (!TrySpawnOrganAugment(owner, augmentProto, out var augment, out var organ))
            {
                continue;
            }

            if (!TryInstallAugment(augment, organ, bodyParts))
            {
                Log.Warning(
                    $"Could not install preset augment '{augmentProto}' (slot '{organ.SlotId}') on {ToPrettyString(owner)}.");
                QueueDel(augment);
            }
        }

        return true;
    }

    private void InstallExplicitEntry(
        EntityUid owner,
        List<(EntityUid Id, BodyPartComponent Component)> bodyParts,
        PresetAugmentEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Slot))
        {
            Log.Warning($"Preset augment entry on {ToPrettyString(owner)} has empty slot.");
            return;
        }

        if (!TrySpawnOrganAugment(owner, entry.Prototype, out var augment, out var organ))
        {
            return;
        }

        if (!string.Equals(organ.SlotId, entry.Slot, StringComparison.Ordinal))
        {
            Log.Warning(
                $"Preset augment '{entry.Prototype}' slot mismatch on {ToPrettyString(owner)}: organ slot '{organ.SlotId}', configured '{entry.Slot}'.");
            QueueDel(augment);
            return;
        }

        if (!TryInstallExplicit(augment, organ, bodyParts, entry))
        {
            Log.Warning(
                $"Could not install preset augment '{entry.Prototype}' into slot '{entry.Slot}' on {ToPrettyString(owner)}.");
            QueueDel(augment);
        }
    }

    private bool TrySpawnOrganAugment(
        EntityUid owner,
        EntProtoId prototype,
        out EntityUid augment,
        out OrganComponent organ)
    {
        augment = Spawn(prototype, Transform(owner).Coordinates);
        if (TryComp<OrganComponent>(augment, out var comp) && comp != null)
        {
            organ = comp;
            return true;
        }

        Log.Warning($"Preset augment '{prototype}' on {ToPrettyString(owner)} is not an organ, deleting.");
        QueueDel(augment);
        organ = default!;
        return false;
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
