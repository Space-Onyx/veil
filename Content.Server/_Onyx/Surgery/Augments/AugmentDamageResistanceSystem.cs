using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Goobstation.Shared.Augments;
using Robust.Shared.Prototypes;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentDamageResistanceSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InstalledAugmentsComponent, DamageModifyEvent>(OnDamageModify);
    }

    private void OnDamageModify(EntityUid uid, InstalledAugmentsComponent component, DamageModifyEvent args)
    {
        foreach (var netEnt in component.InstalledAugments)
        {
            var augUid = GetEntity(netEnt);
            if (!TryComp<AugmentDamageResistanceComponent>(augUid, out var resist))
                continue;

            if (HasComp<AugmentEmpDisabledComponent>(augUid))
                continue;

            if (HasComp<AugmentBrainDeactivatedComponent>(augUid))
                continue;

            if (HasComp<AugmentNeuroManuallyDisabledComponent>(augUid))
                continue;

            if (!_proto.TryIndex<DamageModifierSetPrototype>(resist.DamageModifierSetId, out var modifierSet))
                continue;

            args.Damage = DamageSpecifier.ApplyModifierSet(args.Damage, modifierSet);
        }
    }
}
