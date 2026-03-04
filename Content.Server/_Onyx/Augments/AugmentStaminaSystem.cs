using Content.Shared._Onyx.Augments;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Damage.Components;
using Content.Goobstation.Shared.Augments;

namespace Content.Server._Onyx.Augments;

public sealed class AugmentStaminaSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentStaminaComponent, OrganAddedToBodyEvent>(OnOrganAddedToBody);
        SubscribeLocalEvent<AugmentStaminaComponent, OrganRemovedFromBodyEvent>(OnOrganRemovedFromBody);
    }

    private void OnOrganAddedToBody(EntityUid uid, AugmentStaminaComponent component, ref OrganAddedToBodyEvent args)
    {
        RefreshStamina(args.Body);
    }

    private void OnOrganRemovedFromBody(EntityUid uid, AugmentStaminaComponent component, ref OrganRemovedFromBodyEvent args)
    {
        RefreshStamina(args.OldBody);
    }

    private void RefreshStamina(EntityUid body)
    {
        if (!TryComp<StaminaComponent>(body, out var stamina))
            return;

        var baseCritThreshold = 100f;
        var baseDecay = 5f;

        var totalMult = 1.0f;
        var totalFlat = 0f;
        var recoveryMult = 1.0f;

        var query = EntityQueryEnumerator<OrganComponent, AugmentStaminaComponent>();
        while (query.MoveNext(out _, out var organ, out var aug))
        {
            if (organ.Body != body)
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
