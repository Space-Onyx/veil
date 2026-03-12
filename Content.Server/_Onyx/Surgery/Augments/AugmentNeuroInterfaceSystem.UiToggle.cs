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
using Content.Shared.Containers.ItemSlots;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed partial class AugmentNeuroInterfaceSystem
{
    private void OnToggleAugment(Entity<AugmentNeuroInterfaceComponent> ent, ref NeuroInterfaceToggleAugmentMessage msg)
    {
        if (_augment.GetBody(ent) is not { } body || !CanControlInterface(body, msg.Actor))
            return;

        var isRemoteControl = msg.Actor != body;
        if (isRemoteControl && msg.Enable)
        {
            _popup.PopupEntity(Loc.GetString("neuro-interface-popup-remote-enable-blocked"), msg.Actor, msg.Actor, PopupType.SmallCaution);
            return;
        }

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
            GetAdjustedMaxNeuroLoad(body, ent.Owner, ent.Comp.MaxNeuroLoad),
            augments);
        _ui.SetUiState(ent.Owner, NeuroInterfaceUiKey.Key, state);
    }

    private void OnBulkToggle(Entity<AugmentNeuroInterfaceComponent> ent, ref NeuroInterfaceBulkToggleMessage msg)
    {
        if (_augment.GetBody(ent) is not { } body || !CanControlInterface(body, msg.Actor))
            return;

        var isRemoteControl = msg.Actor != body;
        if (isRemoteControl)
        {
            _popup.PopupEntity(Loc.GetString("neuro-interface-popup-remote-bulk-blocked"), msg.Actor, msg.Actor, PopupType.SmallCaution);
            return;
        }

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

    }

    private List<NeuroInterfaceAugmentEntry> BuildAugmentList(EntityUid body)
    {
        var entries = new List<NeuroInterfaceAugmentEntry>();
        var descriptionCache = new Dictionary<EntityUid, string>();

        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            var category = GetCategory(partComp);

            if (IsControllablePart(partUid))
            {
                entries.Add(BuildPartEntry(partUid, partComp, category, descriptionCache));
            }

            foreach (var (organUid, organComp) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!IsControllableOrgan(organUid))
                    continue;

                entries.Add(BuildOrganEntry(organUid, organComp, partUid, category, body, descriptionCache));
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

    private NeuroInterfaceAugmentEntry BuildPartEntry(
        EntityUid partUid,
        BodyPartComponent partComp,
        NeuroInterfaceBodyCategory category,
        Dictionary<EntityUid, string> descriptionCache)
    {
        var metrics = GetAugmentMetrics(partUid);
        return new NeuroInterfaceAugmentEntry(
            GetNetEntity(partUid),
            GetNetEntity(partUid),
            category,
            Name(partUid),
            IsEnhancementEnabled(partUid, partComp.Enabled),
            partComp.CanEnable
            && AugmentBehaviorPolicyHelpers.CanToggle(partUid, EntityManager)
            && !IsEmpBlocked(partUid),
            false,
            GetStatus(partUid, partComp),
            GetEntityDescriptionCached(partUid, descriptionCache),
            metrics.PassivePowerEntries,
            metrics.ActivePowerEntries,
            metrics.PassiveNeuroLoadEntries,
            metrics.ActiveNeuroLoadEntries,
            new List<NeuroInterfaceModuleEntry>());
    }

    private NeuroInterfaceAugmentEntry BuildOrganEntry(
        EntityUid organUid,
        OrganComponent organComp,
        EntityUid partUid,
        NeuroInterfaceBodyCategory category,
        EntityUid body,
        Dictionary<EntityUid, string> descriptionCache)
    {
        var metrics = GetAugmentMetrics(organUid);
        return new NeuroInterfaceAugmentEntry(
            GetNetEntity(organUid),
            GetNetEntity(partUid),
            category,
            Name(organUid),
            IsEnhancementEnabled(organUid, organComp.Enabled),
            organComp.CanEnable
            && AugmentBehaviorPolicyHelpers.CanToggle(organUid, EntityManager)
            && !HasComp<AugmentNeuroInterfaceComponent>(organUid)
            && !IsEmpBlocked(organUid),
            HasComp<AugmentComponent>(organUid) && HasComp<AugmentNeuroConfigurableComponent>(organUid),
            GetStatus(organUid, organComp, body),
            GetEntityDescriptionCached(organUid, descriptionCache),
            metrics.PassivePowerEntries,
            metrics.ActivePowerEntries,
            metrics.PassiveNeuroLoadEntries,
            metrics.ActiveNeuroLoadEntries,
            GetModuleEntries(organUid, descriptionCache));
    }

    private bool IsEnhancementEnabled(EntityUid uid, bool enabledFlag)
    {
        return enabledFlag
               && (!AugmentBehaviorPolicyHelpers.CanToggle(uid, EntityManager)
                   || !HasComp<AugmentNeuroManuallyDisabledComponent>(uid))
               && (!AugmentBehaviorPolicyHelpers.IsAffectedByBrainDeactivation(uid, EntityManager)
                   || !HasComp<AugmentBrainDeactivatedComponent>(uid));
    }

    private string GetEntityDescriptionCached(EntityUid uid, Dictionary<EntityUid, string> cache)
    {
        if (cache.TryGetValue(uid, out var cached))
            return cached;

        var description = GetEntityDescription(uid);
        cache[uid] = description;
        return description;
    }

    private List<NeuroInterfaceModuleEntry> GetModuleEntries(EntityUid augmentUid, Dictionary<EntityUid, string> descriptionCache)
    {
        var modules = new List<NeuroInterfaceModuleEntry>();
        if (!TryComp<AugmentModuleSlotsComponent>(augmentUid, out var moduleSlots)
            || !TryComp<ItemSlotsComponent>(augmentUid, out var itemSlots))
        {
            return modules;
        }

        foreach (var definition in moduleSlots.Slots)
        {
            if (!_itemSlots.TryGetSlot(augmentUid, definition.Id, out var slot, itemSlots))
                continue;

            if (slot.Item is not { } moduleUid)
                continue;

            var slotName = definition.Name.StartsWith("augment-", StringComparison.Ordinal)
                ? Loc.GetString(definition.Name)
                : definition.Name;

            var metrics = GetAugmentMetrics(moduleUid, forcePowerEnabled: true);

            modules.Add(new NeuroInterfaceModuleEntry(
                GetNetEntity(moduleUid),
                definition.Id,
                slotName,
                Name(moduleUid),
                GetEntityDescriptionCached(moduleUid, descriptionCache),
                metrics.PassivePowerEntries,
                metrics.ActivePowerEntries,
                metrics.PassiveNeuroLoadEntries,
                metrics.ActiveNeuroLoadEntries));
        }

        return modules;
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
        if (!CanToggleEnhancement(body, target, organ.CanEnable, showPopups))
            return;

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
        if (!CanToggleEnhancement(body, target, part.CanEnable, showPopups))
            return;

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

    private bool CanToggleEnhancement(EntityUid body, EntityUid target, bool canEnable, bool showPopups)
    {
        if (!canEnable)
        {
            if (showPopups)
                _popup.PopupEntity(Loc.GetString("neuro-interface-popup-cannot-toggle"), body, body, PopupType.SmallCaution);
            return false;
        }

        if (!AugmentBehaviorPolicyHelpers.CanToggle(target, EntityManager))
        {
            if (showPopups)
                _popup.PopupEntity(Loc.GetString("neuro-interface-popup-cannot-toggle"), body, body, PopupType.SmallCaution);
            return false;
        }

        if (AugmentBehaviorPolicyHelpers.IsAffectedByBrainDeactivation(target, EntityManager)
            && HasComp<AugmentBrainDeactivatedComponent>(target))
        {
            if (showPopups)
                _popup.PopupEntity(Loc.GetString("neuro-interface-popup-brain-blocked"), body, body, PopupType.SmallCaution);
            return false;
        }

        if (IsEmpBlocked(target))
        {
            if (showPopups)
                _popup.PopupEntity(Loc.GetString("neuro-interface-popup-emp-blocked"), body, body, PopupType.SmallCaution);
            return false;
        }

        return true;
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

    private bool CanControlInterface(EntityUid body, EntityUid actor)
    {
        return actor == body || HasAdminRemoteControl(body, actor);
    }

}
