using Content.Shared._Onyx.Augments;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Goobstation.Shared.Augments;

namespace Content.Server._Onyx.Augments;

public sealed class AugmentMovementSpeedSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentMovementSpeedComponent, OrganAddedToBodyEvent>(OnOrganAddedToBody);
        SubscribeLocalEvent<AugmentMovementSpeedComponent, OrganRemovedFromBodyEvent>(OnOrganRemovedFromBody);
        SubscribeLocalEvent<InstalledAugmentsComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
    }

    private void OnOrganAddedToBody(EntityUid uid, AugmentMovementSpeedComponent component, ref OrganAddedToBodyEvent args)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(args.Body);
    }

    private void OnOrganRemovedFromBody(EntityUid uid, AugmentMovementSpeedComponent component, ref OrganRemovedFromBodyEvent args)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(args.OldBody);
    }

    private void OnRefreshMovementSpeed(EntityUid uid, InstalledAugmentsComponent component, RefreshMovementSpeedModifiersEvent args)
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

        var organQuery = EntityQueryEnumerator<OrganComponent, AugmentMovementSpeedComponent>();
        while (organQuery.MoveNext(out _, out var organ, out var augment))
        {
            if (organ.Body != body)
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
}
