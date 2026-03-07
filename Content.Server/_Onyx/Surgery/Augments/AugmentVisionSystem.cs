using Content.Goobstation.Shared.Augments;
using Content.Goobstation.Shared.Overlays;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Chemistry.Components;
using Content.Shared.Flash.Components;
using Content.Shared.Overlays;
using Content.Shared.PowerCell;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentVisionSystem : EntitySystem
{
    [Dependency] private readonly SharedAugmentPowerCellSystem _augmentPower = default!;
    [Dependency] private readonly SharedPowerCellSystem _powerCell = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentVisionComponent, OrganAddedToBodyEvent>(OnOrganAddedToBody);
        SubscribeLocalEvent<AugmentVisionComponent, OrganRemovedFromBodyEvent>(OnOrganRemovedFromBody);
        SubscribeLocalEvent<AugmentVisionComponent, AugmentEmpDisabledEvent>(OnEmpDisabled);
        SubscribeLocalEvent<AugmentVisionComponent, AugmentEmpRestoredEvent>(OnEmpRestored);
        SubscribeLocalEvent<AugmentVisionComponent, AugmentLostPowerEvent>(OnLostPower);
        SubscribeLocalEvent<AugmentVisionComponent, AugmentGainedPowerEvent>(OnGainedPower);
        SubscribeLocalEvent<AugmentVisionComponent, GetAugmentsPowerDrawEvent>(OnGetPowerDraw);

        SubscribeLocalEvent<InstalledAugmentsComponent, SwitchableOverlayToggledEvent>(OnVisionToggled);
    }

    private void OnEmpDisabled(EntityUid uid, AugmentVisionComponent component, ref AugmentEmpDisabledEvent args)
    {
        ApplyVision(args.Body, component, false);
    }

    private void OnEmpRestored(EntityUid uid, AugmentVisionComponent component, ref AugmentEmpRestoredEvent args)
    {
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

        ApplyVision(args.Body, component, true);
    }

    private void OnGetPowerDraw(EntityUid uid, AugmentVisionComponent component, ref GetAugmentsPowerDrawEvent args)
    {
        if (!RequiresPower(uid, component))
            return;

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

        if (hasPassiveVision)
            args.TotalDraw += component.PowerDraw;

        if (hasActiveToggleableVision)
        {
            args.TotalDraw += component.ActivePowerDrawByType.Count > 0
                ? activeDraw
                : component.PowerDraw;
        }
    }

    private void OnVisionToggled(EntityUid uid, InstalledAugmentsComponent component, ref SwitchableOverlayToggledEvent args)
    {
        UpdateBodyDrawRate(uid);
    }

    private void ApplyVision(EntityUid body, AugmentVisionComponent component, bool enable)
    {
        foreach (var visionType in component.GetAllVisionTypes())
        {
            ApplyVisionType(body, component, visionType, enable);
        }
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
                if (enable)
                    EnsureComp<FlashImmunityComponent>(body);
                else
                    RemComp<FlashImmunityComponent>(body);
                break;

            case AugmentVisionType.MedicalHUD:
                if (enable)
                    EnsureComp<ShowHealthIconsComponent>(body);
                else
                    RemComp<ShowHealthIconsComponent>(body);
                break;

            case AugmentVisionType.SecurityHUD:
                if (enable)
                {
                    EnsureComp<ShowJobIconsComponent>(body);
                    EnsureComp<ShowCriminalRecordIconsComponent>(body);
                }
                else
                {
                    RemComp<ShowJobIconsComponent>(body);
                    RemComp<ShowCriminalRecordIconsComponent>(body);
                }
                break;

            case AugmentVisionType.DiagnosticHUD:
                if (enable)
                    EnsureComp<ShowHealthIconsComponent>(body);
                else
                    RemComp<ShowHealthIconsComponent>(body);
                break;

            case AugmentVisionType.SyndicateHUD:
                if (enable)
                    EnsureComp<ShowSyndicateIconsComponent>(body);
                else
                    RemComp<ShowSyndicateIconsComponent>(body);
                break;

            case AugmentVisionType.MindShieldHUD:
                if (enable)
                    EnsureComp<ShowMindShieldIconsComponent>(body);
                else
                    RemComp<ShowMindShieldIconsComponent>(body);
                break;

            case AugmentVisionType.SolutionScanner:
                if (enable)
                    EnsureComp<SolutionScannerComponent>(body);
                else
                    RemComp<SolutionScannerComponent>(body);
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
        if (_augmentPower.GetBodyAugment(body) is not { } slot)
            return false;

        if (!TryComp<PowerCellDrawComponent>(slot, out var draw))
            return false;

        return _powerCell.HasDrawCharge(slot, draw);
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
}
