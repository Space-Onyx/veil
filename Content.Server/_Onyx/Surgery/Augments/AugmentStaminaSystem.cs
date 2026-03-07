using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Body.Events;
using Content.Shared.Damage.Components;
using Content.Goobstation.Shared.Augments;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentStaminaSystem : EntitySystem
{
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
        if (!TryComp<InstalledAugmentsComponent>(body, out var installed))
            return;

        var baseCritThreshold = 100f;
        var baseDecay = 5f;

        var totalMult = 1.0f;
        var totalFlat = 0f;
        var recoveryMult = 1.0f;

        foreach (var netEnt in installed.InstalledAugments)
        {
            var augUid = GetEntity(netEnt);
            if (!TryComp<AugmentStaminaComponent>(augUid, out var aug))
                continue;

            if (HasComp<AugmentEmpDisabledComponent>(augUid))
                continue;

            if (HasComp<AugmentNeuroManuallyDisabledComponent>(augUid))
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

        stamina.CritThreshold = baseCritThreshold * totalMult + totalFlat;
        stamina.Decay = baseDecay * recoveryMult;

        Dirty(body, stamina);
    }
}
