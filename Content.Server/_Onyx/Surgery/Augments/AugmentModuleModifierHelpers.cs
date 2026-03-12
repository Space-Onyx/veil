using System;
using System.Diagnostics.CodeAnalysis;
using Content.Shared._Onyx.Surgery.Augments;
using Robust.Shared.GameObjects;

namespace Content.Server._Onyx.Surgery.Augments;

internal static class AugmentModuleModifierHelpers
{
    public static float GetVisionActivePowerMultiplier(EntityUid uid, IEntityManager entMan)
    {
        if (!TryGetAccumulator(uid, entMan, out var accumulator))
            return AugmentUniversalModuleDefaults.NeutralMultiplier;

        return MathF.Max(0f, accumulator.VisionActivePowerMultiplier);
    }

    public static float GetMaxNeuroLoad(EntityUid uid, IEntityManager entMan)
    {
        if (!TryGetAccumulator(uid, entMan, out var accumulator))
            return 0f;

        return accumulator.MaxNeuroLoad;
    }

    public static float GetPassiveNeuroLoad(EntityUid uid, IEntityManager entMan)
    {
        if (!TryGetAccumulator(uid, entMan, out var accumulator))
            return 0f;

        return accumulator.CurrentNeuroLoad;
    }

    public static float GetVisionActivePower(EntityUid uid, IEntityManager entMan)
    {
        if (!TryGetAccumulator(uid, entMan, out var accumulator))
            return 0f;

        return accumulator.VisionActivePower;
    }

    public static float GetVisionActiveNeuroMultiplier(EntityUid uid, IEntityManager entMan)
    {
        if (!TryGetAccumulator(uid, entMan, out var accumulator))
            return AugmentUniversalModuleDefaults.NeutralMultiplier;

        return MathF.Max(0f, accumulator.VisionActiveNeuroMultiplier);
    }

    public static float GetVisionActiveNeuro(EntityUid uid, IEntityManager entMan)
    {
        if (!TryGetAccumulator(uid, entMan, out var accumulator))
            return 0f;

        return accumulator.VisionActiveNeuro;
    }

    public static float GetItemPanelActivePowerMultiplier(EntityUid uid, IEntityManager entMan)
    {
        if (!TryGetAccumulator(uid, entMan, out var accumulator))
            return AugmentUniversalModuleDefaults.NeutralMultiplier;

        return MathF.Max(0f, accumulator.ItemPanelActivePowerMultiplier);
    }

    public static float GetItemPanelActivePower(EntityUid uid, IEntityManager entMan)
    {
        if (!TryGetAccumulator(uid, entMan, out var accumulator))
            return 0f;

        return accumulator.ItemPanelActivePower;
    }

    public static float GetItemPanelActiveNeuroMultiplier(EntityUid uid, IEntityManager entMan)
    {
        if (!TryGetAccumulator(uid, entMan, out var accumulator))
            return AugmentUniversalModuleDefaults.NeutralMultiplier;

        return MathF.Max(0f, accumulator.ItemPanelActiveNeuroMultiplier);
    }

    public static float GetItemPanelActiveNeuro(EntityUid uid, IEntityManager entMan)
    {
        if (!TryGetAccumulator(uid, entMan, out var accumulator))
            return 0f;

        return accumulator.ItemPanelActiveNeuro;
    }

    public static float ApplyActiveModifiersWithFloor(float baseValue, float multiplier, float delta)
    {
        return AugmentEffectPipeline.ApplyActiveModifierWithFloor(baseValue, multiplier, delta);
    }

    private static bool TryGetAccumulator(
        EntityUid uid,
        IEntityManager entMan,
        [NotNullWhen(true)] out AugmentUniversalModuleAccumulatorComponent? accumulator)
    {
        return entMan.TryGetComponent(uid, out accumulator);
    }
}
