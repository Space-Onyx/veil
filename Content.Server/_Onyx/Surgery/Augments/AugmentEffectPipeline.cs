using System;
using System.Collections.Generic;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;

namespace Content.Server._Onyx.Surgery.Augments;

internal static class AugmentEffectPipeline
{
    public static void CollectUniversalModuleSources(
        Entity<AugmentModuleSlotsComponent> host,
        ItemSlotsSystem itemSlots,
        ItemSlotsComponent itemSlotsComp,
        List<EntityUid> target)
    {
        target.Clear();

        foreach (var definition in host.Comp.Slots)
        {
            if (!itemSlots.TryGetSlot(host, definition.Id, out var slot, itemSlotsComp))
                continue;

            if (slot.Item is { } moduleUid)
                target.Add(moduleUid);
        }
    }

    public static UniversalModuleAggregate AggregateUniversalModules(
        IEntityManager entMan,
        List<EntityUid> moduleSources)
    {
        var aggregate = UniversalModuleAggregate.CreateDefault();

        foreach (var source in moduleSources)
        {
            if (!entMan.TryGetComponent(source, out AugmentUniversalModuleComponent? module))
                continue;

            aggregate.Include(module);
        }

        return aggregate;
    }

    public static float ApplyActiveModifierWithFloor(float baseValue, float multiplier, float delta)
    {
        if (baseValue <= 0f)
            return 0f;

        var value = baseValue * MathF.Max(0f, multiplier) + delta;
        if (multiplier < 1f || delta < 0f)
            return MathF.Max(1f, value);

        return MathF.Max(0f, value);
    }
}

internal struct UniversalModuleAggregate
{
    public float MaxNeuroLoad;
    public float CurrentNeuroLoad;
    public float PassivePowerDraw;
    public float VisionActivePowerMultiplier;
    public float VisionActivePower;
    public float VisionActiveNeuroMultiplier;
    public float VisionActiveNeuro;
    public float ItemPanelActivePowerMultiplier;
    public float ItemPanelActivePower;
    public float ItemPanelActiveNeuroMultiplier;
    public float ItemPanelActiveNeuro;
    public string NeuroLoadTooltipSource;
    public string PowerTooltipSource;

    public static UniversalModuleAggregate CreateDefault()
    {
        return new UniversalModuleAggregate
        {
            VisionActivePowerMultiplier = AugmentUniversalModuleDefaults.NeutralMultiplier,
            VisionActiveNeuroMultiplier = AugmentUniversalModuleDefaults.NeutralMultiplier,
            ItemPanelActivePowerMultiplier = AugmentUniversalModuleDefaults.NeutralMultiplier,
            ItemPanelActiveNeuroMultiplier = AugmentUniversalModuleDefaults.NeutralMultiplier,
            NeuroLoadTooltipSource = AugmentUniversalModuleDefaults.PassiveNeuroTooltipSource,
            PowerTooltipSource = AugmentUniversalModuleDefaults.PassivePowerTooltipSource,
        };
    }

    public void Include(AugmentUniversalModuleComponent module)
    {
        MaxNeuroLoad += module.MaxNeuroLoad;
        CurrentNeuroLoad += module.CurrentNeuroLoad;
        PassivePowerDraw += module.PassivePowerDraw;
        VisionActivePowerMultiplier *= module.VisionActivePowerMultiplier;
        VisionActivePower += module.VisionActivePower;
        VisionActiveNeuroMultiplier *= module.VisionActiveNeuroMultiplier;
        VisionActiveNeuro += module.VisionActiveNeuro;
        ItemPanelActivePowerMultiplier *= module.ItemPanelActivePowerMultiplier;
        ItemPanelActivePower += module.ItemPanelActivePower;
        ItemPanelActiveNeuroMultiplier *= module.ItemPanelActiveNeuroMultiplier;
        ItemPanelActiveNeuro += module.ItemPanelActiveNeuro;
        NeuroLoadTooltipSource = module.NeuroLoadTooltipSource;
        PowerTooltipSource = module.PowerTooltipSource;
    }

    public bool IsNeutral()
    {
        return MaxNeuroLoad == 0f
               && CurrentNeuroLoad == 0f
               && PassivePowerDraw == 0f
               && VisionActivePowerMultiplier == AugmentUniversalModuleDefaults.NeutralMultiplier
               && VisionActivePower == 0f
               && VisionActiveNeuroMultiplier == AugmentUniversalModuleDefaults.NeutralMultiplier
               && VisionActiveNeuro == 0f
               && ItemPanelActivePowerMultiplier == AugmentUniversalModuleDefaults.NeutralMultiplier
               && ItemPanelActivePower == 0f
               && ItemPanelActiveNeuroMultiplier == AugmentUniversalModuleDefaults.NeutralMultiplier
               && ItemPanelActiveNeuro == 0f;
    }

    public void ApplyTo(AugmentUniversalModuleAccumulatorComponent accumulator)
    {
        accumulator.MaxNeuroLoad = MaxNeuroLoad;
        accumulator.CurrentNeuroLoad = CurrentNeuroLoad;
        accumulator.PassivePowerDraw = PassivePowerDraw;
        accumulator.VisionActivePowerMultiplier = VisionActivePowerMultiplier;
        accumulator.VisionActivePower = VisionActivePower;
        accumulator.VisionActiveNeuroMultiplier = VisionActiveNeuroMultiplier;
        accumulator.VisionActiveNeuro = VisionActiveNeuro;
        accumulator.ItemPanelActivePowerMultiplier = ItemPanelActivePowerMultiplier;
        accumulator.ItemPanelActivePower = ItemPanelActivePower;
        accumulator.ItemPanelActiveNeuroMultiplier = ItemPanelActiveNeuroMultiplier;
        accumulator.ItemPanelActiveNeuro = ItemPanelActiveNeuro;
        accumulator.NeuroLoadTooltipSource = NeuroLoadTooltipSource;
        accumulator.PowerTooltipSource = PowerTooltipSource;
    }
}
