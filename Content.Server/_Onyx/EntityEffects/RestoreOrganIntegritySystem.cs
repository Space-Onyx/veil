using Content.Goobstation.Maths.FixedPoint;
using Content.Shared._Onyx.EntityEffects.Effects;
using Content.Shared._Shitmed.Medical.Surgery.Traumas.Systems;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.EntityEffects;

namespace Content.Server._Onyx.EntityEffects;

public sealed class RestoreOrganIntegritySystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly TraumaSystem _trauma = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ExecuteEntityEffectEvent<RestoreOrganIntegrity>>(OnExecuteRestoreOrganIntegrity);
    }

    private void OnExecuteRestoreOrganIntegrity(ref ExecuteEntityEffectEvent<RestoreOrganIntegrity> args)
    {
        if (args.Args is not EntityEffectReagentArgs reagentArgs)
            return;

        var amount = reagentArgs.Quantity * reagentArgs.Scale;
        if (amount <= 0)
            return;

        if (!EntityManager.ComponentFactory.TryGetRegistration(args.Effect.Organ, out var registration))
            return;

        foreach (var organEnt in _body.GetBodyOrgans(args.Args.TargetEntity))
        {
            if (!HasComp(organEnt.Id, registration.Type))
                continue;

            var organ = organEnt.Component;
            if (organ.OrganIntegrity >= organ.IntegrityCap)
                continue;

            var healing = organ.IntegrityCap * args.Effect.PercentPerUnit * amount;
            var desiredIntegrity = FixedPoint2.Min(organ.IntegrityCap, organ.OrganIntegrity + healing);
            var change = desiredIntegrity - organ.OrganIntegrity;
            if (change <= 0)
                continue;

            ChangeOrganIntegrityContribution(organEnt.Id, organ, args.Args.TargetEntity, args.Effect.Identifier, change);
        }
    }

    private void ChangeOrganIntegrityContribution(EntityUid organUid, OrganComponent organ, EntityUid effectOwner, string identifier, FixedPoint2 change)
    {
        if (organ.IntegrityModifiers.Count == 0)
        {
            var desiredIntegrity = FixedPoint2.Min(organ.IntegrityCap, organ.OrganIntegrity + change);
            if (!_trauma.TrySetOrganDamageModifier(organUid, desiredIntegrity, effectOwner, identifier, organ))
                _trauma.TryCreateOrganDamageModifier(organUid, desiredIntegrity, effectOwner, identifier, organ);

            return;
        }

        var selectedIdentifier = string.Empty;
        var selectedOwner = EntityUid.Invalid;
        var selectedValue = FixedPoint2.Zero;
        var found = false;

        foreach (var (key, value) in organ.IntegrityModifiers)
        {
            selectedIdentifier = key.Item1;
            selectedOwner = key.Item2;
            selectedValue = value;
            found = true;

            if (key == (identifier, effectOwner))
                break;
        }

        if (!found)
            return;

        var newValue = selectedValue + change;
        if (newValue == 0)
        {
            _trauma.TryRemoveOrganDamageModifier(organUid, selectedOwner, selectedIdentifier, organ);
            return;
        }

        if (!_trauma.TrySetOrganDamageModifier(organUid, newValue, selectedOwner, selectedIdentifier, organ))
            _trauma.TryChangeOrganDamageModifier(organUid, change, selectedOwner, selectedIdentifier, organ);
    }
}
