using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Body.Events;
using Content.Server.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Emp;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.PowerCell;
using Content.Goobstation.Shared.Augments;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentMovementSpeedSystem : EntitySystem
{
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly SharedAugmentPowerCellSystem _augmentPower = default!;
    [Dependency] private readonly SharedPowerCellSystem _powerCell = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentMovementSpeedComponent, OrganAddedToBodyEvent>(OnOrganAddedToBody);
        SubscribeLocalEvent<AugmentMovementSpeedComponent, OrganRemovedFromBodyEvent>(OnOrganRemovedFromBody);
        SubscribeLocalEvent<InstalledAugmentsComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<AugmentMovementSpeedComponent, AugmentEmpDisabledEvent>(OnEmpDisabled);
        SubscribeLocalEvent<AugmentMovementSpeedComponent, AugmentEmpRestoredEvent>(OnEmpRestored);
        SubscribeLocalEvent<AugmentMovementSpeedComponent, AugmentManuallyDisabledEvent>(OnManuallyDisabled);
        SubscribeLocalEvent<AugmentMovementSpeedComponent, AugmentManuallyRestoredEvent>(OnManuallyRestored);
        SubscribeLocalEvent<AugmentMovementSpeedComponent, AugmentLostPowerEvent>(OnLostPower);
        SubscribeLocalEvent<AugmentMovementSpeedComponent, AugmentGainedPowerEvent>(OnGainedPower);
        SubscribeLocalEvent<AugmentMovementSpeedComponent, GetAugmentsPowerDrawEvent>(OnGetPowerDraw);
        SubscribeLocalEvent<AugmentMovementSpeedComponent, CollectAugmentNeuroInterfaceMetricsEvent>(OnCollectMetrics);
    }

    private void OnOrganAddedToBody(EntityUid uid, AugmentMovementSpeedComponent component, ref OrganAddedToBodyEvent args)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(args.Body);
        UpdateBodyDrawRate(args.Body);
    }

    private void OnOrganRemovedFromBody(EntityUid uid, AugmentMovementSpeedComponent component, ref OrganRemovedFromBodyEvent args)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(args.OldBody);
        UpdateBodyDrawRate(args.OldBody);
    }

    private void OnEmpDisabled(EntityUid uid, AugmentMovementSpeedComponent component, ref AugmentEmpDisabledEvent args)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(args.Body);
    }

    private void OnEmpRestored(EntityUid uid, AugmentMovementSpeedComponent component, ref AugmentEmpRestoredEvent args)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(args.Body);
    }

    private void OnManuallyDisabled(EntityUid uid, AugmentMovementSpeedComponent component, ref AugmentManuallyDisabledEvent args)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(args.Body);
    }

    private void OnManuallyRestored(EntityUid uid, AugmentMovementSpeedComponent component, ref AugmentManuallyRestoredEvent args)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(args.Body);
    }

    private void OnLostPower(EntityUid uid, AugmentMovementSpeedComponent component, ref AugmentLostPowerEvent args)
    {
        if (!RequiresPower(uid, component))
            return;

        _movementSpeed.RefreshMovementSpeedModifiers(args.Body);
    }

    private void OnGainedPower(EntityUid uid, AugmentMovementSpeedComponent component, ref AugmentGainedPowerEvent args)
    {
        if (!RequiresPower(uid, component))
            return;

        _movementSpeed.RefreshMovementSpeedModifiers(args.Body);
    }

    private void OnGetPowerDraw(EntityUid uid, AugmentMovementSpeedComponent component, ref GetAugmentsPowerDrawEvent args)
    {
        if (IsEnhancementDisabled(uid))
            return;

        if (RequiresPower(uid, component))
            args.TotalDraw += component.PowerDraw;
    }

    private void OnCollectMetrics(EntityUid uid, AugmentMovementSpeedComponent component, ref CollectAugmentNeuroInterfaceMetricsEvent args)
    {
        if (!args.PowerEnabled || !component.RequiresPower || component.PowerDraw <= 0f)
            return;

        args.PassivePowerEntries.Add(new NeuroInterfaceMetricEntry("neuro-interface-tooltip-source-power-movement", component.PowerDraw));
    }

    private void OnRefreshMovementSpeed(EntityUid uid, InstalledAugmentsComponent _, RefreshMovementSpeedModifiersEvent args)
    {
        var (walkMult, sprintMult) = CalculateTotalSpeedModifier(uid);

        if (walkMult != 1.0f || sprintMult != 1.0f)
        {
            args.ModifySpeed(walkMult, sprintMult);
        }
    }
    private (float WalkMult, float SprintMult) CalculateTotalSpeedModifier(EntityUid body)
    {
        var totalWalkMult = 1.0f;
        var totalSprintMult = 1.0f;
        var totalWalkFlat = 0f;
        var totalSprintFlat = 0f;
        var hasPower = HasAugmentPower(body);

        foreach (var enhancement in AugmentEnhancementHelpers.EnumerateEnhancements(body, _body, _itemSlots, EntityManager))
        {
            if (!TryComp<AugmentMovementSpeedComponent>(enhancement, out var augment))
                continue;

            if (!CanApplySpeedModifier(enhancement, augment, hasPower))
                continue;

            switch (augment.ModifierType)
            {
                case SpeedModifierType.Percentage:
                    totalWalkMult *= augment.WalkMultiplier;
                    totalSprintMult *= augment.SprintMultiplier;
                    break;
                case SpeedModifierType.Flat:
                    totalWalkFlat += augment.WalkAddition;
                    totalSprintFlat += augment.SprintAddition;
                    break;
            }
        }

        if ((totalWalkFlat != 0f || totalSprintFlat != 0f)
            && TryComp<MovementSpeedModifierComponent>(body, out var moveSpeed))
        {
            if (moveSpeed.BaseWalkSpeed > 0f)
                totalWalkMult += totalWalkFlat / moveSpeed.BaseWalkSpeed;

            if (moveSpeed.BaseSprintSpeed > 0f)
                totalSprintMult += totalSprintFlat / moveSpeed.BaseSprintSpeed;
        }

        return (totalWalkMult, totalSprintMult);
    }

    private bool CanApplySpeedModifier(EntityUid uid, AugmentMovementSpeedComponent component, bool hasPower)
    {
        if (IsEnhancementDisabled(uid))
            return false;

        return !RequiresPower(uid, component) || hasPower;
    }

    private bool IsEnhancementDisabled(EntityUid uid)
    {
        return HasComp<AugmentEmpDisabledComponent>(uid)
               || HasComp<AugmentBrainDeactivatedComponent>(uid)
               || HasComp<AugmentNeuroManuallyDisabledComponent>(uid);
    }

    private bool HasAugmentPower(EntityUid body)
    {
        return AugmentPowerHelpers.HasAugmentPower(body, _augmentPower, _powerCell, EntityManager);
    }

    private bool RequiresPower(EntityUid uid, AugmentMovementSpeedComponent component)
    {
        return component.RequiresPower
            && component.PowerDraw > 0f
            && (!TryComp<AugmentPowerConfigComponent>(uid, out var globalConfig) || globalConfig.RequiresPower);
    }

    private void UpdateBodyDrawRate(EntityUid body)
    {
        if (_augmentPower.GetBodyAugment(body) is { } slot)
            _augmentPower.UpdateDrawRate(slot.Owner);
    }
}
