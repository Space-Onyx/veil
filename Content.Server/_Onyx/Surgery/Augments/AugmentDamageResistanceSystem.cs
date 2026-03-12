using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Goobstation.Shared.Augments;
using Content.Server.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Prototypes;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentDamageResistanceSystem : EntitySystem
{
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InstalledAugmentsComponent, DamageModifyEvent>(OnDamageModify);
    }

    private void OnDamageModify(EntityUid uid, InstalledAugmentsComponent component, DamageModifyEvent args)
    {
        foreach (var enhancement in AugmentEnhancementHelpers.EnumerateEnhancements(uid, _body, _itemSlots, EntityManager))
        {
            if (!TryComp<AugmentDamageResistanceComponent>(enhancement, out var resist))
                continue;

            if (!CanApplyResistance(enhancement))
                continue;

            if (!_proto.TryIndex<DamageModifierSetPrototype>(resist.DamageModifierSetId, out var modifierSet))
                continue;

            args.Damage = DamageSpecifier.ApplyModifierSet(args.Damage, modifierSet);
        }
    }

    private bool CanApplyResistance(EntityUid enhancement)
    {
        var empBlocked = AugmentBehaviorPolicyHelpers.IsAffectedByEmp(enhancement, EntityManager)
                         && HasComp<AugmentEmpDisabledComponent>(enhancement);
        var brainBlocked = AugmentBehaviorPolicyHelpers.IsAffectedByBrainDeactivation(enhancement, EntityManager)
                           && HasComp<AugmentBrainDeactivatedComponent>(enhancement);
        var manuallyDisabled = AugmentBehaviorPolicyHelpers.CanToggle(enhancement, EntityManager)
                               && HasComp<AugmentNeuroManuallyDisabledComponent>(enhancement);

        return !empBlocked
               && !brainBlocked
               && !manuallyDisabled;
    }
}

