using Content.Shared._Onyx.Augments;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Goobstation.Shared.Augments;
using Robust.Shared.Prototypes;

namespace Content.Server._Onyx.Augments;

public sealed class AugmentDamageResistanceSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentDamageResistanceComponent, OrganAddedToBodyEvent>(OnOrganAddedToBody);
        SubscribeLocalEvent<AugmentDamageResistanceComponent, OrganRemovedFromBodyEvent>(OnOrganRemovedFromBody);
        SubscribeLocalEvent<InstalledAugmentsComponent, DamageModifyEvent>(OnDamageModify);
    }

    private void OnOrganAddedToBody(EntityUid uid, AugmentDamageResistanceComponent component, ref OrganAddedToBodyEvent args)
    {
    }

    private void OnOrganRemovedFromBody(EntityUid uid, AugmentDamageResistanceComponent component, ref OrganRemovedFromBodyEvent args)
    {
    }

    private void OnDamageModify(EntityUid uid, InstalledAugmentsComponent component, DamageModifyEvent args)
    {
        var organQuery = EntityQueryEnumerator<OrganComponent, AugmentDamageResistanceComponent>();
        while (organQuery.MoveNext(out _, out var organ, out var resist))
        {
            if (organ.Body != uid)
                continue;

            if (!_proto.TryIndex<DamageModifierSetPrototype>(resist.DamageModifierSetId, out var modifierSet))
                continue;

            args.Damage = DamageSpecifier.ApplyModifierSet(args.Damage, modifierSet);
        }
    }
}
