using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Body.Events;
using Content.Shared.Damage.Components;
using Content.Goobstation.Shared.Augments;
using Content.Server.Body.Systems;
using Content.Shared.Containers.ItemSlots;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentStaminaSystem : EntitySystem
{
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    private const float BaseCritThreshold = 100f;
    private const float BaseDecay = 5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentStaminaComponent, OrganAddedToBodyEvent>(OnOrganAddedToBody);
        SubscribeLocalEvent<AugmentStaminaComponent, OrganRemovedFromBodyEvent>(OnOrganRemovedFromBody);
        SubscribeLocalEvent<AugmentStaminaComponent, AugmentEmpDisabledEvent>(OnEmpDisabled);
        SubscribeLocalEvent<AugmentStaminaComponent, AugmentEmpRestoredEvent>(OnEmpRestored);
        SubscribeLocalEvent<AugmentStaminaComponent, AugmentManuallyDisabledEvent>(OnManuallyDisabled);
        SubscribeLocalEvent<AugmentStaminaComponent, AugmentManuallyRestoredEvent>(OnManuallyRestored);
    }

    private void OnOrganAddedToBody(EntityUid uid, AugmentStaminaComponent component, ref OrganAddedToBodyEvent args)
    {
        RefreshStamina(args.Body);
    }

    private void OnOrganRemovedFromBody(EntityUid uid, AugmentStaminaComponent component, ref OrganRemovedFromBodyEvent args)
    {
        RefreshStamina(args.OldBody);
    }

    private void OnEmpDisabled(EntityUid uid, AugmentStaminaComponent component, ref AugmentEmpDisabledEvent args)
    {
        RefreshStamina(args.Body);
    }

    private void OnEmpRestored(EntityUid uid, AugmentStaminaComponent component, ref AugmentEmpRestoredEvent args)
    {
        RefreshStamina(args.Body);
    }

    private void OnManuallyDisabled(EntityUid uid, AugmentStaminaComponent component, ref AugmentManuallyDisabledEvent args)
    {
        RefreshStamina(args.Body);
    }

    private void OnManuallyRestored(EntityUid uid, AugmentStaminaComponent component, ref AugmentManuallyRestoredEvent args)
    {
        RefreshStamina(args.Body);
    }

    private void RefreshStamina(EntityUid body)
    {
        if (!TryComp<StaminaComponent>(body, out var stamina))
            return;

        var totalMult = 1.0f;
        var totalFlat = 0f;
        var recoveryMult = 1.0f;

        foreach (var enhancement in AugmentEnhancementHelpers.EnumerateEnhancements(body, _body, _itemSlots, EntityManager))
        {
            if (!TryComp<AugmentStaminaComponent>(enhancement, out var aug))
                continue;

            if (!CanApplyStaminaModifier(enhancement))
                continue;

            switch (aug.ModifierType)
            {
                case StaminaModifierType.Percentage:
                    totalMult *= aug.StaminaMultiplier;
                    break;
                case StaminaModifierType.Flat:
                    totalFlat += aug.StaminaAddition;
                    break;
            }

            recoveryMult *= aug.RecoveryMultiplier;
        }

        stamina.CritThreshold = BaseCritThreshold * totalMult + totalFlat;
        stamina.Decay = BaseDecay * recoveryMult;

        Dirty(body, stamina);
    }

    private bool CanApplyStaminaModifier(EntityUid enhancement)
    {
        return !HasComp<AugmentEmpDisabledComponent>(enhancement)
               && !HasComp<AugmentBrainDeactivatedComponent>(enhancement)
               && !HasComp<AugmentNeuroManuallyDisabledComponent>(enhancement);
    }
}
