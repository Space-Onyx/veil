using Content.Shared._Onyx.Surgery.Augments;
using Robust.Shared.GameObjects;

namespace Content.Server._Onyx.Surgery.Augments;

internal static class AugmentBehaviorPolicyHelpers
{
    public static bool CanToggle(EntityUid uid, IEntityManager entMan)
    {
        return !entMan.TryGetComponent<AugmentBehaviorPolicyComponent>(uid, out var policy) || policy.CanToggle;
    }

    public static bool IsAffectedByBrainDeactivation(EntityUid uid, IEntityManager entMan)
    {
        return !entMan.TryGetComponent<AugmentBehaviorPolicyComponent>(uid, out var policy) || policy.AffectedByBrainDeactivation;
    }

    public static bool IsAffectedByEmp(EntityUid uid, IEntityManager entMan)
    {
        return !entMan.TryGetComponent<AugmentBehaviorPolicyComponent>(uid, out var policy) || policy.AffectedByEmp;
    }

    public static bool IsAffectedBySuppression(EntityUid uid, IEntityManager entMan)
    {
        return !entMan.TryGetComponent<AugmentBehaviorPolicyComponent>(uid, out var policy) || policy.AffectedBySuppression;
    }
}
