using System;
using System.Collections.Generic;
using Content.Goobstation.Maths.FixedPoint;
using Content.Goobstation.Shared.Overlays;
using Content.Server.Body.Components;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Popups;
using Content.Shared._Shitmed.Medical.Surgery.Traumas.Systems;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed partial class AugmentNeuroInterfaceSystem
{
    private float GetCurrentNeuroLoad(EntityUid body)
    {
        var load = 0f;

        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            if (IsPartActive(partUid, partComp))
                load += GetPassiveNeuroLoad(partUid);

            foreach (var (organUid, organComp) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!IsOrganActive(organUid, organComp))
                    continue;

                load += GetPassiveNeuroLoad(organUid);

                if (TryComp<AugmentItemPanelComponent>(organUid, out var panel) && panel.IsEquipped)
                {
                    load += AugmentModuleModifierHelpers.ApplyActiveModifiersWithFloor(
                        panel.EquippedNeuroLoad,
                        AugmentModuleModifierHelpers.GetItemPanelActiveNeuroMultiplier(organUid, EntityManager),
                        AugmentModuleModifierHelpers.GetItemPanelActiveNeuro(organUid, EntityManager));
                }

                if (TryComp<AugmentVisionComponent>(organUid, out var vision))
                    load += GetActiveVisionNeuroLoad(body, organUid, vision);

                load += GetActiveModuleVisionNeuroLoad(body, organUid);
            }
        }

        return load;
    }

    private float GetPassiveNeuroLoad(EntityUid uid)
    {
        var baseLoad = 0f;
        var moduleNeuroLoad = 0f;

        if (TryComp<AugmentNeuroLoadComponent>(uid, out var load))
            baseLoad += load.PassiveLoad;

        moduleNeuroLoad += AugmentModuleModifierHelpers.GetPassiveNeuroLoad(uid, EntityManager);

        var modified = baseLoad + moduleNeuroLoad;
        if (moduleNeuroLoad < 0f && baseLoad > 0f)
            return MathF.Max(1f, modified);

        return MathF.Max(0f, modified);
    }

    private AugmentMetricBreakdown GetAugmentMetrics(EntityUid uid, bool? forcePowerEnabled = null)
    {
        var metrics = new AugmentMetricBreakdown();
        var powerEnabled = forcePowerEnabled
            ?? (!TryComp<AugmentPowerConfigComponent>(uid, out var powerConfig)
                || powerConfig.RequiresPower);
        var ev = new CollectAugmentNeuroInterfaceMetricsEvent(
            powerEnabled,
            metrics.PassivePowerEntries,
            metrics.ActivePowerEntries,
            metrics.PassiveNeuroLoadEntries,
            metrics.ActiveNeuroLoadEntries);
        RaiseLocalEvent(uid, ref ev);

        return metrics;
    }

    private string GetEntityDescription(EntityUid uid)
    {
        if (!TryComp<Robust.Shared.GameObjects.MetaDataComponent>(uid, out var meta))
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(meta.EntityDescription))
            return meta.EntityDescription;

        if (meta.EntityPrototype != null && !string.IsNullOrWhiteSpace(meta.EntityPrototype.Description))
            return meta.EntityPrototype.Description;

        return string.Empty;
    }

    private bool IsPartActive(EntityUid partUid, BodyPartComponent part)
    {
        return part.Enabled
               && (!AugmentBehaviorPolicyHelpers.CanToggle(partUid, EntityManager)
                   || !HasComp<AugmentNeuroManuallyDisabledComponent>(partUid))
               && !IsEmpBlocked(partUid);
    }

    private bool IsOrganActive(EntityUid organUid, OrganComponent organ)
    {
        return organ.Enabled
               && (!AugmentBehaviorPolicyHelpers.CanToggle(organUid, EntityManager)
                   || !HasComp<AugmentNeuroManuallyDisabledComponent>(organUid))
               && !IsEmpBlocked(organUid);
    }

    private float GetActiveVisionNeuroLoad(EntityUid body, EntityUid augmentUid, AugmentVisionComponent vision)
    {
        var load = 0f;

        foreach (var type in vision.GetAllVisionTypes())
        {
            if (!AugmentVisionComponent.IsToggleable(type))
                continue;

            if (!IsVisionTypeActive(body, type))
                continue;

            var active = vision.GetActiveNeuroLoad(type);
            load += AugmentModuleModifierHelpers.ApplyActiveModifiersWithFloor(
                active,
                AugmentModuleModifierHelpers.GetVisionActiveNeuroMultiplier(augmentUid, EntityManager),
                AugmentModuleModifierHelpers.GetVisionActiveNeuro(augmentUid, EntityManager));
        }

        return load;
    }

    private float GetActiveModuleVisionNeuroLoad(EntityUid body, EntityUid hostAugmentUid)
    {
        if (!TryComp<AugmentModuleSlotsComponent>(hostAugmentUid, out var moduleSlots)
            || !TryComp<ItemSlotsComponent>(hostAugmentUid, out var itemSlots))
        {
            return 0f;
        }

        var load = 0f;
        foreach (var definition in moduleSlots.Slots)
        {
            if (!_itemSlots.TryGetSlot(hostAugmentUid, definition.Id, out var slot, itemSlots)
                || slot.Item is not { } moduleUid
                || !TryComp<AugmentVisionComponent>(moduleUid, out var moduleVision))
            {
                continue;
            }

            foreach (var type in moduleVision.GetAllVisionTypes())
            {
                if (!AugmentVisionComponent.IsToggleable(type))
                    continue;

                if (!IsVisionTypeActive(body, type))
                    continue;

                var active = moduleVision.GetActiveNeuroLoad(type);
                load += AugmentModuleModifierHelpers.ApplyActiveModifiersWithFloor(
                    active,
                    AugmentModuleModifierHelpers.GetVisionActiveNeuroMultiplier(hostAugmentUid, EntityManager),
                    AugmentModuleModifierHelpers.GetVisionActiveNeuro(hostAugmentUid, EntityManager));
            }
        }

        return load;
    }

    private bool IsVisionTypeActive(EntityUid body, AugmentVisionType type)
    {
        return type switch
        {
            AugmentVisionType.NightVision => TryComp<NightVisionComponent>(body, out var nv) && nv.IsActive,
            AugmentVisionType.ThermalVision => TryComp<ThermalVisionComponent>(body, out var tv) && tv.IsActive,
            _ => false,
        };
    }

    private bool TryGetNeuroLoadLimit(EntityUid body, out float maxLoad)
    {
        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            foreach (var (organUid, organComp) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!TryComp<AugmentNeuroInterfaceComponent>(organUid, out var neuro))
                    continue;

                maxLoad = GetAdjustedMaxNeuroLoad(body, organUid, neuro.MaxNeuroLoad);
                return true;
            }
        }

        maxLoad = GetAdjustedMaxNeuroLoad(body, body, DefaultNeuroLoadWithoutInterface);
        return true;
    }

    private void ApplyOverloadDamage(EntityUid body)
    {
        var now = _timing.CurTime;
        var currentLoad = GetCurrentNeuroLoad(body);
        TryGetNeuroLoadLimit(body, out var maxLoad);

        var overload = currentLoad - maxLoad;
        if (overload <= 0f)
        {
            ClearOverloadDamageModifiers(body);
            return;
        }

        if (!TryComp<BodyComponent>(body, out var bodyComp)
            || !_body.TryGetBodyOrganEntityComps<BrainComponent>((body, bodyComp), out var brains))
        {
            return;
        }

        var damage = (FixedPoint2)(overload * NeuroOverloadDamagePerSecond * (float)OverloadDamageInterval.TotalSeconds);
        if (damage <= 0)
        {
            ClearOverloadDamageModifiers(body);
            return;
        }

        var effectOwner = GetNeuroLoadEffectOwner(body);
        foreach (var brain in brains)
        {
            ApplyBrainIntegrityDamage(brain, effectOwner, damage);
        }

        if (!_nextBrainOverloadPopup.TryGetValue(body, out var nextPopupAt) || now >= nextPopupAt)
        {
            _nextBrainOverloadPopup[body] = now + BrainOverloadPopupInterval;
            _popup.PopupEntity(Loc.GetString("neuro-interface-popup-brain-overload-damage"), body, body, PopupType.SmallCaution);
        }
    }

    private void ClearOverloadDamageModifiers(EntityUid body)
    {
        if (!TryComp<BodyComponent>(body, out var bodyComp)
            || !_body.TryGetBodyOrganEntityComps<BrainComponent>((body, bodyComp), out var brains))
        {
            return;
        }

        foreach (var brain in brains)
        {
            if (brain.Comp2.IntegrityModifiers.Count == 0)
                continue;

            var toRemove = new List<EntityUid>();
            foreach (var (key, _) in brain.Comp2.IntegrityModifiers)
            {
                if (key.Item1 == NeuroLoadOverloadIdentifier)
                    toRemove.Add(key.Item2);
            }

            foreach (var effectOwner in toRemove)
            {
                _trauma.TryRemoveOrganDamageModifier(brain.Owner, effectOwner, NeuroLoadOverloadIdentifier, brain.Comp2);
            }
        }
    }

    private void ApplyBrainIntegrityDamage(Entity<BrainComponent, OrganComponent> brain, EntityUid effectOwner, FixedPoint2 damage)
    {
        if (damage <= 0)
            return;

        var currentIntegrity = brain.Comp2.OrganIntegrity;
        var desiredIntegrity = FixedPoint2.Max(FixedPoint2.Zero, currentIntegrity - damage);
        if (desiredIntegrity == currentIntegrity)
            return;

        var sumWithoutOverload = FixedPoint2.Zero;
        foreach (var (key, value) in brain.Comp2.IntegrityModifiers)
        {
            if (key == (NeuroLoadOverloadIdentifier, effectOwner))
                continue;

            sumWithoutOverload += value;
        }

        var overloadContribution = desiredIntegrity - sumWithoutOverload;

        if (desiredIntegrity <= 0 && overloadContribution == 0)
            overloadContribution = -1;

        if (overloadContribution == 0)
        {
            _trauma.TryRemoveOrganDamageModifier(brain.Owner, effectOwner, NeuroLoadOverloadIdentifier, brain.Comp2);
            return;
        }

        if (!_trauma.TrySetOrganDamageModifier(brain.Owner, overloadContribution, effectOwner, NeuroLoadOverloadIdentifier, brain.Comp2))
        {
            if (!_trauma.TryCreateOrganDamageModifier(brain.Owner, overloadContribution, effectOwner, NeuroLoadOverloadIdentifier, brain.Comp2))
                _trauma.TryChangeOrganDamageModifier(brain.Owner, overloadContribution, effectOwner, NeuroLoadOverloadIdentifier, brain.Comp2);
        }
    }

    private EntityUid GetNeuroLoadEffectOwner(EntityUid body)
    {
        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            foreach (var (organUid, _) in _body.GetPartOrgans(partUid, partComp))
            {
                if (HasComp<AugmentNeuroInterfaceComponent>(organUid))
                    return organUid;
            }
        }

        return body;
    }
}
