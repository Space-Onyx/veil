using Content.Shared._Shitmed.Cybernetics;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Body.Organ;
using Robust.Shared.GameObjects;

namespace Content.Shared._Onyx.Surgery.Augments;

public static class CyberneticOrganHelper
{
    public static bool HasOperationalCyberneticOrgan<TMarker>(this IEntityManager entityManager, EntityUid body)
        where TMarker : IComponent
    {
        if (!entityManager.TryGetComponent<BodyComponent>(body, out var bodyComp))
            return false;

        var bodySystem = entityManager.System<SharedBodySystem>();
        foreach (var (organUid, organ) in bodySystem.GetBodyOrgans(body, bodyComp))
        {
            if (!organ.Enabled)
                continue;

            if (!entityManager.TryGetComponent<CyberneticsComponent>(organUid, out var cybernetics)
                || cybernetics.Disabled)
            {
                continue;
            }

            if (!entityManager.TryGetComponent<TMarker>(organUid, out _))
                continue;

            return true;
        }

        return false;
    }

    public static bool HasOperationalCyberneticOrgan(this IEntityManager entityManager, EntityUid body)
    {
        if (!entityManager.TryGetComponent<BodyComponent>(body, out var bodyComp))
            return false;

        var bodySystem = entityManager.System<SharedBodySystem>();
        foreach (var (organUid, organ) in bodySystem.GetBodyOrgans(body, bodyComp))
        {
            if (!organ.Enabled)
                continue;

            if (!entityManager.TryGetComponent<CyberneticsComponent>(organUid, out var cybernetics)
                || cybernetics.Disabled)
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
