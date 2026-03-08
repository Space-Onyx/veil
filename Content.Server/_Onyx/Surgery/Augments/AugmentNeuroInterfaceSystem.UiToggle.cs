using System;
using System.Collections.Generic;
using Content.Goobstation.Shared.Augments;
using Content.Server.Power.Components;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Popups;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._Shitmed.Body.Organ;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed partial class AugmentNeuroInterfaceSystem
{
    private void OnToggleAugment(Entity<AugmentNeuroInterfaceComponent> ent, ref NeuroInterfaceToggleAugmentMessage msg)
    {
        if (_augment.GetBody(ent) is not { } body || !CanControlInterface(ent, body, msg.Actor))
            return;

        var isRemoteControl = msg.Actor != body;
        var target = GetEntity(msg.Augment);

        if (target == ent.Owner)
            return;

        if (TryComp<OrganComponent>(target, out var organ)
            && organ.Body == body
            && IsControllableOrgan(target))
        {
            ToggleOrgan(body, target, organ, msg.Enable, true);
        }
        else if (TryComp<BodyPartComponent>(target, out var part)
            && part.Body == body
            && IsControllablePart(target))
        {
            TogglePart(body, target, part, msg.Enable, true);
        }
        else
        {
            return;
        }

        UpdatePowerDraw(body);
        UpdateUi(ent, body);

        if (isRemoteControl)
            ApplyForeignInterfaceManipulationPenalty(msg.Actor);
    }

    private void UpdateUi(Entity<AugmentNeuroInterfaceComponent> ent, EntityUid body)
    {
        var augments = BuildAugmentList(body);
        var (powerSourceName, powerOutputPerSecond) = GetPowerSourceInfo(body);
        var powerConsumptionPerSecond = _augmentPower.GetBodyDraw(body);

        var hasBattery = false;
        var charge = 0f;
        var maxCharge = 0f;

        if (_augmentPower.GetBodyAugment(body) is { } slot
            && _powerCell.TryGetBatteryFromSlot(slot.Owner, out _, out BatteryComponent? battery))
        {
            hasBattery = true;
            charge = battery.CurrentCharge;
            maxCharge = battery.MaxCharge;
        }

        var state = new NeuroInterfaceBuiState(
            ent.Comp.InterfaceCode,
            powerSourceName,
            powerOutputPerSecond,
            powerConsumptionPerSecond,
            hasBattery,
            charge,
            maxCharge,
            GetCurrentNeuroLoad(body),
            GetAdjustedMaxNeuroLoad(body, ent.Comp.MaxNeuroLoad),
            augments);
        _ui.SetUiState(ent.Owner, NeuroInterfaceUiKey.Key, state);
    }

    private void OnBulkToggle(Entity<AugmentNeuroInterfaceComponent> ent, ref NeuroInterfaceBulkToggleMessage msg)
    {
        if (_augment.GetBody(ent) is not { } body || !CanControlInterface(ent, body, msg.Actor))
            return;

        var isRemoteControl = msg.Actor != body;
        switch (msg.Target)
        {
            case NeuroInterfaceBulkTarget.Implants:
                ToggleAllOrgans(body, msg.Enable);
                break;
            case NeuroInterfaceBulkTarget.Limbs:
                ToggleAllParts(body, msg.Enable);
                break;
            case NeuroInterfaceBulkTarget.All:
                ToggleAllOrgans(body, msg.Enable);
                ToggleAllParts(body, msg.Enable);
                break;
        }

        UpdatePowerDraw(body);
        UpdateUi(ent, body);

        if (isRemoteControl)
            ApplyForeignInterfaceManipulationPenalty(msg.Actor);
    }

    private List<NeuroInterfaceAugmentEntry> BuildAugmentList(EntityUid body)
    {
        var entries = new List<NeuroInterfaceAugmentEntry>();

        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            var category = GetCategory(partComp);

            if (IsControllablePart(partUid))
            {
                var partEnabled = partComp.Enabled
                                  && !HasComp<AugmentNeuroManuallyDisabledComponent>(partUid)
                                  && !HasComp<AugmentBrainDeactivatedComponent>(partUid);
                var partName = Name(partUid);
                var partStatus = GetStatus(partUid, partComp);
                var partDescription = GetEntityDescription(partUid);
                var partMetrics = GetAugmentMetrics(partUid);

                entries.Add(new NeuroInterfaceAugmentEntry(
                    GetNetEntity(partUid),
                    GetNetEntity(partUid),
                    category,
                    partName,
                    partEnabled,
                    partComp.CanEnable && !IsEmpBlocked(partUid),
                    false,
                    partStatus,
                    partDescription,
                    partMetrics.PassivePowerEntries,
                    partMetrics.ActivePowerEntries,
                    partMetrics.PassiveNeuroLoadEntries,
                    partMetrics.ActiveNeuroLoadEntries));
            }

            foreach (var (organUid, organComp) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!IsControllableOrgan(organUid))
                    continue;

                var enabled = organComp.Enabled
                              && !HasComp<AugmentNeuroManuallyDisabledComponent>(organUid)
                              && !HasComp<AugmentBrainDeactivatedComponent>(organUid);
                var canToggle = organComp.CanEnable
                                && !HasComp<AugmentNeuroInterfaceComponent>(organUid)
                                && !IsEmpBlocked(organUid);
                var canConfigure = HasComp<AugmentComponent>(organUid) && HasComp<AugmentNeuroConfigurableComponent>(organUid);
                var name = Name(organUid);
                var status = GetStatus(organUid, organComp, body);
                var description = GetEntityDescription(organUid);
                var metrics = GetAugmentMetrics(organUid);

                entries.Add(new NeuroInterfaceAugmentEntry(
                    GetNetEntity(organUid),
                    GetNetEntity(partUid),
                    category,
                    name,
                    enabled,
                    canToggle,
                    canConfigure,
                    status,
                    description,
                    metrics.PassivePowerEntries,
                    metrics.ActivePowerEntries,
                    metrics.PassiveNeuroLoadEntries,
                    metrics.ActiveNeuroLoadEntries));
            }
        }

        entries.Sort((a, b) =>
        {
            var indexA = GetCategoryRank(a.Category);
            var indexB = GetCategoryRank(b.Category);

            if (indexA != indexB)
                return indexA.CompareTo(indexB);

            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return entries;
    }

    private static int GetCategoryRank(NeuroInterfaceBodyCategory category)
    {
        return category switch
        {
            NeuroInterfaceBodyCategory.Head => 0,
            NeuroInterfaceBodyCategory.Torso => 1,
            NeuroInterfaceBodyCategory.RightArm => 2,
            NeuroInterfaceBodyCategory.LeftArm => 3,
            NeuroInterfaceBodyCategory.RightHand => 4,
            NeuroInterfaceBodyCategory.LeftHand => 5,
            NeuroInterfaceBodyCategory.Groin => 6,
            NeuroInterfaceBodyCategory.RightLeg => 7,
            NeuroInterfaceBodyCategory.LeftLeg => 8,
            NeuroInterfaceBodyCategory.RightFoot => 9,
            NeuroInterfaceBodyCategory.LeftFoot => 10,
            _ => int.MaxValue,
        };
    }

    private NeuroInterfaceBodyCategory GetCategory(BodyPartComponent part)
    {
        return (part.PartType, part.Symmetry) switch
        {
            (BodyPartType.Head, _) => NeuroInterfaceBodyCategory.Head,
            (BodyPartType.Chest, _) => NeuroInterfaceBodyCategory.Torso,
            (BodyPartType.Arm, BodyPartSymmetry.Right) => NeuroInterfaceBodyCategory.RightArm,
            (BodyPartType.Arm, BodyPartSymmetry.Left) => NeuroInterfaceBodyCategory.LeftArm,
            (BodyPartType.Hand, BodyPartSymmetry.Right) => NeuroInterfaceBodyCategory.RightHand,
            (BodyPartType.Hand, BodyPartSymmetry.Left) => NeuroInterfaceBodyCategory.LeftHand,
            (BodyPartType.Groin, _) => NeuroInterfaceBodyCategory.Groin,
            (BodyPartType.Leg, BodyPartSymmetry.Right) => NeuroInterfaceBodyCategory.RightLeg,
            (BodyPartType.Leg, BodyPartSymmetry.Left) => NeuroInterfaceBodyCategory.LeftLeg,
            (BodyPartType.Foot, BodyPartSymmetry.Right) => NeuroInterfaceBodyCategory.RightFoot,
            (BodyPartType.Foot, BodyPartSymmetry.Left) => NeuroInterfaceBodyCategory.LeftFoot,
            _ => NeuroInterfaceBodyCategory.Torso,
        };
    }

    private void ToggleOrgan(EntityUid body, EntityUid target, OrganComponent organ, bool enable, bool showPopups)
    {
        if (!organ.CanEnable)
        {
            if (showPopups)
                _popup.PopupEntity(Loc.GetString("neuro-interface-popup-cannot-toggle"), body, body, PopupType.SmallCaution);
            return;
        }

        if (HasComp<AugmentBrainDeactivatedComponent>(target))
        {
            if (showPopups)
                _popup.PopupEntity(Loc.GetString("neuro-interface-popup-brain-blocked"), body, body, PopupType.SmallCaution);
            return;
        }

        if (IsEmpBlocked(target))
        {
            if (showPopups)
                _popup.PopupEntity(Loc.GetString("neuro-interface-popup-emp-blocked"), body, body, PopupType.SmallCaution);
            return;
        }

        var manuallyDisabled = HasComp<AugmentNeuroManuallyDisabledComponent>(target);

        if (enable)
        {
            var enabledEv = new OrganEnableChangedEvent(true);
            RaiseLocalEvent(target, ref enabledEv);

            if (!manuallyDisabled)
                return;

            RemComp<AugmentNeuroManuallyDisabledComponent>(target);
            var restoredEv = new AugmentManuallyRestoredEvent(body);
            RaiseLocalEvent(target, ref restoredEv);
            return;
        }

        var disabledEv = new OrganEnableChangedEvent(false);
        RaiseLocalEvent(target, ref disabledEv);

        if (manuallyDisabled)
            return;

        EnsureComp<AugmentNeuroManuallyDisabledComponent>(target);
        var manualDisabledEv = new AugmentManuallyDisabledEvent(body);
        RaiseLocalEvent(target, ref manualDisabledEv);
    }

    private void TogglePart(EntityUid body, EntityUid target, BodyPartComponent part, bool enable, bool showPopups)
    {
        if (!part.CanEnable)
        {
            if (showPopups)
                _popup.PopupEntity(Loc.GetString("neuro-interface-popup-cannot-toggle"), body, body, PopupType.SmallCaution);
            return;
        }

        if (HasComp<AugmentBrainDeactivatedComponent>(target))
        {
            if (showPopups)
                _popup.PopupEntity(Loc.GetString("neuro-interface-popup-brain-blocked"), body, body, PopupType.SmallCaution);
            return;
        }

        if (IsEmpBlocked(target))
        {
            if (showPopups)
                _popup.PopupEntity(Loc.GetString("neuro-interface-popup-emp-blocked"), body, body, PopupType.SmallCaution);
            return;
        }

        var manuallyDisabled = HasComp<AugmentNeuroManuallyDisabledComponent>(target);

        if (enable)
        {
            var enabledEv = new BodyPartEnableChangedEvent(true);
            RaiseLocalEvent(target, ref enabledEv);

            if (!manuallyDisabled)
                return;

            RemComp<AugmentNeuroManuallyDisabledComponent>(target);
            return;
        }

        var disabledEv = new BodyPartEnableChangedEvent(false);
        RaiseLocalEvent(target, ref disabledEv);

        if (manuallyDisabled)
            return;

        EnsureComp<AugmentNeuroManuallyDisabledComponent>(target);
    }

    private void ToggleAllOrgans(EntityUid body, bool enable)
    {
        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            foreach (var (organUid, organComp) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!IsControllableOrgan(organUid))
                    continue;

                if (HasComp<AugmentNeuroInterfaceComponent>(organUid))
                    continue;

                ToggleOrgan(body, organUid, organComp, enable, false);
            }
        }
    }

    private void ToggleAllParts(EntityUid body, bool enable)
    {
        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            if (!IsControllablePart(partUid))
                continue;

            TogglePart(body, partUid, partComp, enable, false);
        }
    }

    private static bool CanControlInterface(Entity<AugmentNeuroInterfaceComponent> ent, EntityUid body, EntityUid actor)
    {
        return actor == body || ent.Comp.AuthorizedRemoteViewers.Contains(actor);
    }
}
