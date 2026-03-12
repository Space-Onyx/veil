using Content.Goobstation.Shared.Augments;
using Content.Server.Power.Components;
using Content.Shared.PowerCell;
using Robust.Shared.GameObjects;

namespace Content.Server._Onyx.Surgery.Augments;

internal static class AugmentPowerHelpers
{
    public static bool HasAugmentPower(
        EntityUid body,
        SharedAugmentPowerCellSystem augmentPower,
        SharedPowerCellSystem powerCell,
        IEntityManager entMan)
    {
        if (augmentPower.GetBodyAugment(body) is not { } slot)
            return false;

        if (!entMan.TryGetComponent<PowerCellDrawComponent>(slot.Owner, out var draw))
            return false;

        return powerCell.HasDrawCharge(slot.Owner, draw);
    }
}
