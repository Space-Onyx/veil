using System;
using System.Collections.Generic;
using Content.Goobstation.Shared.Augments;
using Content.Goobstation.Shared.Disease.Components;
using Content.Goobstation.Shared.Overlays;
using Content.Shared.Damage.Prototypes;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Chemistry.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Flash.Components;
using Content.Shared.Overlays;
using Content.Shared.PowerCell;
using Robust.Shared.Prototypes;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentVisionSystem : EntitySystem
{
    [Dependency] private readonly SharedAugmentPowerCellSystem _augmentPower = default!;
    [Dependency] private readonly SharedPowerCellSystem _powerCell = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentVisionComponent, OrganAddedToBodyEvent>(OnOrganAddedToBody);
        SubscribeLocalEvent<AugmentVisionComponent, OrganRemovedFromBodyEvent>(OnOrganRemovedFromBody);
        SubscribeLocalEvent<AugmentVisionComponent, AugmentEmpDisabledEvent>(OnEmpDisabled);
        SubscribeLocalEvent<AugmentVisionComponent, AugmentEmpRestoredEvent>(OnEmpRestored);
        SubscribeLocalEvent<AugmentVisionComponent, AugmentManuallyDisabledEvent>(OnManuallyDisabled);
        SubscribeLocalEvent<AugmentVisionComponent, AugmentManuallyRestoredEvent>(OnManuallyRestored);
        SubscribeLocalEvent<AugmentVisionComponent, AugmentLostPowerEvent>(OnLostPower);
        SubscribeLocalEvent<AugmentVisionComponent, AugmentGainedPowerEvent>(OnGainedPower);
        SubscribeLocalEvent<AugmentVisionComponent, GetAugmentsPowerDrawEvent>(OnGetPowerDraw);
        SubscribeLocalEvent<AugmentVisionComponent, CollectAugmentNeuroInterfaceMetricsEvent>(OnCollectMetrics);

        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, OrganAddedToBodyEvent>(OnModuleHostAddedToBody);
        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, OrganRemovedFromBodyEvent>(OnModuleHostRemovedFromBody);
        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, AugmentModuleInsertedEvent>(OnModuleInserted);
        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, AugmentModuleRemovedEvent>(OnModuleRemoved);
        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, AugmentEmpDisabledEvent>(OnModuleHostEmpDisabled);
        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, AugmentEmpRestoredEvent>(OnModuleHostEmpRestored);
        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, AugmentManuallyDisabledEvent>(OnModuleHostManuallyDisabled);
        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, AugmentManuallyRestoredEvent>(OnModuleHostManuallyRestored);
        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, AugmentLostPowerEvent>(OnModuleHostLostPower);
        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, AugmentGainedPowerEvent>(OnModuleHostGainedPower);
        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, GetAugmentsPowerDrawEvent>(OnModuleHostGetPowerDraw);

        SubscribeLocalEvent<InstalledAugmentsComponent, SwitchableOverlayToggledEvent>(OnVisionToggled);
    }

    private void OnEmpDisabled(EntityUid uid, AugmentVisionComponent component, ref AugmentEmpDisabledEvent args)
    {
        ApplyVision(args.Body, component, false);
    }

    private void OnEmpRestored(EntityUid uid, AugmentVisionComponent component, ref AugmentEmpRestoredEvent args)
    {
        if (IsNeuroDisabled(uid))
            return;

        if (!RequiresPower(uid, component) || HasAugmentPower(args.Body))
            ApplyVision(args.Body, component, true);
    }

    private void OnManuallyDisabled(EntityUid uid, AugmentVisionComponent component, ref AugmentManuallyDisabledEvent args)
    {
        ApplyVision(args.Body, component, false);
    }

    private void OnManuallyRestored(EntityUid uid, AugmentVisionComponent component, ref AugmentManuallyRestoredEvent args)
    {
        if (IsHardDisabled(uid, includeManual: false))
            return;

        if (!RequiresPower(uid, component) || HasAugmentPower(args.Body))
            ApplyVision(args.Body, component, true);
    }

    private void OnOrganAddedToBody(EntityUid uid, AugmentVisionComponent component, ref OrganAddedToBodyEvent args)
    {
        if (!RequiresPower(uid, component) || HasAugmentPower(args.Body))
            ApplyVision(args.Body, component, true);
        else
            ApplyVision(args.Body, component, false);

        UpdateBodyDrawRate(args.Body);
    }

    private void OnOrganRemovedFromBody(EntityUid uid, AugmentVisionComponent component, ref OrganRemovedFromBodyEvent args)
    {
        ApplyVision(args.OldBody, component, false);
        UpdateBodyDrawRate(args.OldBody);
    }

    private void OnLostPower(EntityUid uid, AugmentVisionComponent component, ref AugmentLostPowerEvent args)
    {
        if (RequiresPower(uid, component))
            ApplyVision(args.Body, component, false);
    }

    private void OnGainedPower(EntityUid uid, AugmentVisionComponent component, ref AugmentGainedPowerEvent args)
    {
        if (!RequiresPower(uid, component))
            return;

        if (IsHardDisabled(uid))
            return;

        ApplyVision(args.Body, component, true);
    }

    private void OnGetPowerDraw(EntityUid uid, AugmentVisionComponent component, ref GetAugmentsPowerDrawEvent args)
    {
        if (!RequiresPower(uid, component))
            return;

        if (IsHardDisabled(uid))
            return;

        AddVisionPowerDraw(uid, component, ref args);
    }

    private void OnModuleHostGetPowerDraw(Entity<AugmentNeuroInterfaceComponent> ent, ref GetAugmentsPowerDrawEvent args)
    {
        if (IsHardDisabled(ent.Owner))
        {
            return;
        }

        if (TryComp<OrganComponent>(ent.Owner, out var hostOrgan) && !hostOrgan.Enabled)
            return;

        if (!TryComp<AugmentModuleSlotsComponent>(ent, out var moduleSlots)
            || !TryComp<ItemSlotsComponent>(ent, out var itemSlots))
            return;

        var hasPower = HasAugmentPower(args.Body);
        foreach (var definition in moduleSlots.Slots)
        {
            if (!_itemSlots.TryGetSlot(ent, definition.Id, out var slot, itemSlots)
                || slot.Item is not { } moduleUid
                || !TryComp<AugmentVisionComponent>(moduleUid, out var vision))
            {
                continue;
            }

            if (RequiresPower(ent.Owner, vision) && !hasPower)
                continue;

            AddVisionPowerDraw(ent.Owner, vision, ref args);
        }
    }

    private void AddVisionPowerDraw(EntityUid modifierSource, AugmentVisionComponent component, ref GetAugmentsPowerDrawEvent args)
    {
        var hasPassiveVision = false;
        var hasActiveToggleableVision = false;
        var activeDraw = 0f;

        foreach (var visionType in component.GetAllVisionTypes())
        {
            if (!AugmentVisionComponent.IsToggleable(visionType))
            {
                hasPassiveVision = true;
                continue;
            }

            if (IsToggleableVisionActive(args.Body, visionType))
            {
                hasActiveToggleableVision = true;
                if (component.ActivePowerDrawByType.Count > 0)
                    activeDraw += component.GetActivePowerDraw(visionType);
            }
        }

        if (component.OverlayTypes.Count > 0)
            hasPassiveVision = true;

        if (hasPassiveVision)
            args.TotalDraw += component.PowerDraw;

        if (hasActiveToggleableVision)
        {
            var activePower = component.ActivePowerDrawByType.Count > 0
                ? activeDraw
                : component.PowerDraw;
            args.TotalDraw += AugmentModuleModifierHelpers.ApplyActiveModifiersWithFloor(
                activePower,
                AugmentModuleModifierHelpers.GetVisionActivePowerMultiplier(modifierSource, EntityManager),
                AugmentModuleModifierHelpers.GetVisionActivePower(modifierSource, EntityManager));
        }
    }

    private void OnCollectMetrics(EntityUid uid, AugmentVisionComponent component, ref CollectAugmentNeuroInterfaceMetricsEvent args)
    {
        var hasPassiveVisionType = false;
        foreach (var type in component.GetAllVisionTypes())
        {
            if (!AugmentVisionComponent.IsToggleable(type))
            {
                hasPassiveVisionType = true;
                break;
            }
        }

        if (component.OverlayTypes.Count > 0)
            hasPassiveVisionType = true;

        if (args.PowerEnabled && component.RequiresPower && component.PowerDraw > 0f && hasPassiveVisionType)
            args.PassivePowerEntries.Add(new NeuroInterfaceMetricEntry("neuro-interface-tooltip-source-power-vision-passive", component.PowerDraw));

        foreach (var type in component.GetAllVisionTypes())
        {
            if (!AugmentVisionComponent.IsToggleable(type))
                continue;

            if (args.PowerEnabled && component.RequiresPower)
            {
                var draw = component.ActivePowerDrawByType.Count > 0
                    ? component.GetActivePowerDraw(type)
                    : component.PowerDraw;
                draw = AugmentModuleModifierHelpers.ApplyActiveModifiersWithFloor(
                    draw,
                    AugmentModuleModifierHelpers.GetVisionActivePowerMultiplier(uid, EntityManager),
                    AugmentModuleModifierHelpers.GetVisionActivePower(uid, EntityManager));
                if (draw > 0f)
                    args.ActivePowerEntries.Add(new NeuroInterfaceMetricEntry(GetVisionActivePowerLocKey(type), draw));
            }

            var neuro = component.GetActiveNeuroLoad(type);
            neuro = AugmentModuleModifierHelpers.ApplyActiveModifiersWithFloor(
                neuro,
                AugmentModuleModifierHelpers.GetVisionActiveNeuroMultiplier(uid, EntityManager),
                AugmentModuleModifierHelpers.GetVisionActiveNeuro(uid, EntityManager));
            if (neuro > 0f)
                args.ActiveNeuroLoadEntries.Add(new NeuroInterfaceMetricEntry(GetVisionActiveNeuroLocKey(type), neuro));
        }
    }

    private void OnVisionToggled(EntityUid uid, InstalledAugmentsComponent component, ref SwitchableOverlayToggledEvent args)
    {
        UpdateBodyDrawRate(uid);
    }

    private void OnModuleHostAddedToBody(Entity<AugmentNeuroInterfaceComponent> ent, ref OrganAddedToBodyEvent args)
    {
        ApplyVisionForHostModules(ent, args.Body, true);
    }

    private void OnModuleHostRemovedFromBody(Entity<AugmentNeuroInterfaceComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        ApplyVisionForHostModules(ent, args.OldBody, false);
    }

    private void OnModuleInserted(Entity<AugmentNeuroInterfaceComponent> ent, ref AugmentModuleInsertedEvent args)
    {
        if (args.Body is not { } body || !TryComp<AugmentVisionComponent>(args.Module, out var vision))
            return;

        if (CanEnableModuleVision(ent.Owner, body, vision))
            ApplyVision(body, vision, true);

        UpdateBodyDrawRate(body);
    }

    private void OnModuleRemoved(Entity<AugmentNeuroInterfaceComponent> ent, ref AugmentModuleRemovedEvent args)
    {
        if (args.Body is not { } body || !TryComp<AugmentVisionComponent>(args.Module, out var vision))
            return;

        ApplyVision(body, vision, false);
        UpdateBodyDrawRate(body);
    }

    private void OnModuleHostEmpDisabled(Entity<AugmentNeuroInterfaceComponent> ent, ref AugmentEmpDisabledEvent args)
    {
        ApplyVisionForHostModules(ent, args.Body, false);
    }

    private void OnModuleHostEmpRestored(Entity<AugmentNeuroInterfaceComponent> ent, ref AugmentEmpRestoredEvent args)
    {
        ApplyVisionForHostModules(ent, args.Body, true);
    }

    private void OnModuleHostManuallyDisabled(Entity<AugmentNeuroInterfaceComponent> ent, ref AugmentManuallyDisabledEvent args)
    {
        ApplyVisionForHostModules(ent, args.Body, false);
    }

    private void OnModuleHostManuallyRestored(Entity<AugmentNeuroInterfaceComponent> ent, ref AugmentManuallyRestoredEvent args)
    {
        ApplyVisionForHostModules(ent, args.Body, true);
    }

    private void OnModuleHostLostPower(Entity<AugmentNeuroInterfaceComponent> ent, ref AugmentLostPowerEvent args)
    {
        ApplyVisionForHostModules(ent, args.Body, false);
    }

    private void OnModuleHostGainedPower(Entity<AugmentNeuroInterfaceComponent> ent, ref AugmentGainedPowerEvent args)
    {
        ApplyVisionForHostModules(ent, args.Body, true);
    }

    private void ApplyVisionForHostModules(Entity<AugmentNeuroInterfaceComponent> ent, EntityUid body, bool enable)
    {
        ForEachVisionModule(ent, (moduleUid, vision) =>
        {
            var shouldEnable = enable && CanEnableModuleVision(ent.Owner, body, vision);
            ApplyVision(body, vision, shouldEnable);
        });

        UpdateBodyDrawRate(body);
    }

    private bool CanEnableModuleVision(EntityUid hostAugment, EntityUid body, AugmentVisionComponent component)
    {
        if (IsHardDisabled(hostAugment))
        {
            return false;
        }

        if (TryComp<OrganComponent>(hostAugment, out var organ) && !organ.Enabled)
            return false;

        return !RequiresPower(hostAugment, component) || HasAugmentPower(body);
    }

    private bool IsNeuroDisabled(EntityUid uid)
    {
        return HasComp<AugmentBrainDeactivatedComponent>(uid)
               || HasComp<AugmentNeuroManuallyDisabledComponent>(uid);
    }

    private bool IsHardDisabled(EntityUid uid, bool includeManual = true)
    {
        if (HasComp<AugmentEmpDisabledComponent>(uid) || HasComp<AugmentBrainDeactivatedComponent>(uid))
            return true;

        return includeManual && HasComp<AugmentNeuroManuallyDisabledComponent>(uid);
    }

    private void ForEachVisionModule(Entity<AugmentNeuroInterfaceComponent> ent, Action<EntityUid, AugmentVisionComponent> action)
    {
        if (!TryComp<AugmentModuleSlotsComponent>(ent, out var moduleSlots)
            || !TryComp<ItemSlotsComponent>(ent, out var itemSlots))
            return;

        foreach (var definition in moduleSlots.Slots)
        {
            if (!_itemSlots.TryGetSlot(ent, definition.Id, out var slot, itemSlots))
                continue;

            if (slot.Item is not { } moduleUid || !TryComp<AugmentVisionComponent>(moduleUid, out var moduleVision))
                continue;

            action(moduleUid, moduleVision);
        }
    }

    private void ApplyVision(EntityUid body, AugmentVisionComponent component, bool enable)
    {
        foreach (var visionType in component.GetAllVisionTypes())
        {
            ApplyVisionType(body, component, visionType, enable);
        }

        ApplyOverlays(body, component, enable);
    }

    private void ApplyVisionType(EntityUid body, AugmentVisionComponent component, AugmentVisionType visionType, bool enable)
    {
        switch (visionType)
        {
            case AugmentVisionType.NightVision:
                ToggleNightVision(body, component, enable);
                break;

            case AugmentVisionType.ThermalVision:
                ToggleThermalVision(body, component, enable);
                break;

            case AugmentVisionType.FlashProtection:
                ToggleComponent<FlashImmunityComponent>(body, enable);
                break;

            case AugmentVisionType.MedicalHUD:
                break;

            case AugmentVisionType.SecurityHUD:
                break;

            case AugmentVisionType.DiagnosticHUD:
                break;

            case AugmentVisionType.SyndicateHUD:
                break;

            case AugmentVisionType.MindShieldHUD:
                ToggleComponent<ShowMindShieldIconsComponent>(body, enable);
                break;

            case AugmentVisionType.SolutionScanner:
                ToggleComponent<SolutionScannerComponent>(body, enable);
                break;
        }
    }

    private void ApplyOverlays(EntityUid body, AugmentVisionComponent component, bool enable)
    {
        foreach (var overlayType in component.OverlayTypes)
        {
            ApplyOverlay(body, overlayType, component, enable);
        }
    }

    private void ApplyOverlay(EntityUid body, AugmentVisionOverlayType overlayType, AugmentVisionComponent component, bool enable)
    {
        switch (overlayType)
        {
            case AugmentVisionOverlayType.HealthBars:
                if (enable)
                {
                    var bars = EnsureComp<ShowHealthBarsComponent>(body);
                    bars.DamageContainers = new List<ProtoId<DamageContainerPrototype>>(component.HealthBarDamageContainers);
                    Dirty(body, bars);
                }
                else
                {
                    RemComp<ShowHealthBarsComponent>(body);
                }
                break;

            case AugmentVisionOverlayType.HealthIcons:
                if (enable)
                {
                    var icons = EnsureComp<ShowHealthIconsComponent>(body);
                    icons.DamageContainers = new List<ProtoId<DamageContainerPrototype>>(component.HealthIconDamageContainers);
                    Dirty(body, icons);
                }
                else
                {
                    RemComp<ShowHealthIconsComponent>(body);
                }
                break;

            case AugmentVisionOverlayType.DiseaseIcons:
                ToggleComponent<ShowDiseaseIconsComponent>(body, enable);
                break;

            case AugmentVisionOverlayType.JobIcons:
                ToggleComponent<ShowJobIconsComponent>(body, enable);
                break;

            case AugmentVisionOverlayType.CriminalRecordIcons:
                ToggleComponent<ShowCriminalRecordIconsComponent>(body, enable);
                break;

            case AugmentVisionOverlayType.MindShieldIcons:
                ToggleComponent<ShowMindShieldIconsComponent>(body, enable);
                break;

            case AugmentVisionOverlayType.SyndicateIcons:
                ToggleComponent<ShowSyndicateIconsComponent>(body, enable);
                break;
        }
    }

    private void ToggleNightVision(EntityUid uid, AugmentVisionComponent augment, bool enable)
    {
        if (enable)
        {
            var settings = augment.GetSettings(AugmentVisionType.NightVision);
            var comp = EnsureComp<NightVisionComponent>(uid);
            comp.IsEquipment = false;
            comp.FlashDurationMultiplier = settings.FlashDurationMultiplier;
            comp.PulseTime = settings.PulseTime;
            comp.DrawOverlay = settings.DrawOverlay;
            comp.OverlayOpacity = settings.OverlayOpacity;
            Dirty(uid, comp);
        }
        else
        {
            RemComp<NightVisionComponent>(uid);
        }
    }

    private void ToggleThermalVision(EntityUid uid, AugmentVisionComponent augment, bool enable)
    {
        if (enable)
        {
            var settings = augment.GetSettings(AugmentVisionType.ThermalVision);
            var comp = EnsureComp<ThermalVisionComponent>(uid);
            comp.IsEquipment = false;
            comp.FlashDurationMultiplier = settings.FlashDurationMultiplier;
            comp.PulseTime = settings.PulseTime;
            comp.DrawOverlay = settings.DrawOverlay;
            comp.OverlayOpacity = settings.OverlayOpacity;
            comp.LightRadius = settings.LightRadius;
            Dirty(uid, comp);
        }
        else
        {
            RemComp<ThermalVisionComponent>(uid);
        }
    }

    private void UpdateBodyDrawRate(EntityUid body)
    {
        if (_augmentPower.GetBodyAugment(body) is { } slot)
            _augmentPower.UpdateDrawRate(slot.Owner);
    }

    private bool RequiresPower(EntityUid uid, AugmentVisionComponent component)
    {
        var hasActiveTypeDraw = false;
        foreach (var draw in component.ActivePowerDrawByType.Values)
        {
            if (draw > 0f)
            {
                hasActiveTypeDraw = true;
                break;
            }
        }

        return component.RequiresPower
            && (component.PowerDraw > 0f || hasActiveTypeDraw)
            && (!TryComp<AugmentPowerConfigComponent>(uid, out var globalConfig) || globalConfig.RequiresPower);
    }

    private bool HasAugmentPower(EntityUid body)
    {
        return AugmentPowerHelpers.HasAugmentPower(body, _augmentPower, _powerCell, EntityManager);
    }

    private bool IsToggleableVisionActive(EntityUid body, AugmentVisionType type)
    {
        return type switch
        {
            AugmentVisionType.NightVision => TryComp<NightVisionComponent>(body, out var nv) && nv.IsActive,
            AugmentVisionType.ThermalVision => TryComp<ThermalVisionComponent>(body, out var tv) && tv.IsActive,
            _ => false,
        };
    }

    private static string GetVisionActivePowerLocKey(AugmentVisionType type)
    {
        return type switch
        {
            AugmentVisionType.NightVision => "neuro-interface-tooltip-source-power-vision-night",
            AugmentVisionType.ThermalVision => "neuro-interface-tooltip-source-power-vision-thermal",
            _ => "neuro-interface-tooltip-source-power-vision-active",
        };
    }

    private static string GetVisionActiveNeuroLocKey(AugmentVisionType type)
    {
        return type switch
        {
            AugmentVisionType.NightVision => "neuro-interface-tooltip-source-neuro-vision-night",
            AugmentVisionType.ThermalVision => "neuro-interface-tooltip-source-neuro-vision-thermal",
            _ => "neuro-interface-tooltip-source-neuro-vision-active",
        };
    }

    private void ToggleComponent<T>(EntityUid uid, bool enabled) where T : Component, new()
    {
        if (enabled)
            EnsureComp<T>(uid);
        else
            RemComp<T>(uid);
    }
}
