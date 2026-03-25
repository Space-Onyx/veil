using Content.Server.Emp;
using Content.Shared._Shitmed.Cybernetics;
using Content.Shared.Body.Part;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.Surgery.Augments;

internal static class CyberDeckScriptDisruptionHelper
{
    public static bool TryDisableCybernetic(
        EntityUid cybernetic,
        TimeSpan disabledUntil,
        EmpSystem emp,
        IGameTiming timing,
        IEntityManager entMan)
    {
        if (!entMan.TryGetComponent<CyberneticsComponent>(cybernetic, out var cyberComp))
            return false;

        if (cyberComp.Disabled)
            return false;

        var duration = MathF.Max(0.01f, (float) (disabledUntil - timing.CurTime).TotalSeconds);
        emp.DoEmpEffects(cybernetic, 0f, duration);

        return entMan.TryGetComponent<CyberneticsComponent>(cybernetic, out cyberComp) && cyberComp.Disabled;
    }

    public static bool IsCyberneticBodyPartType(
        EntityUid entity,
        BodyPartType type,
        IEntityManager entMan)
    {
        return entMan.HasComponent<CyberneticsComponent>(entity)
               && entMan.TryGetComponent<BodyPartComponent>(entity, out var part)
               && part.PartType == type;
    }
}
