using Content.Goobstation.Shared.Augments;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Containers.ItemSlots;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentUniversalModuleSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedAugmentPowerCellSystem _augmentPower = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentModuleSlotsComponent, ComponentStartup>(OnSlotsStartup);
        SubscribeLocalEvent<AugmentModuleSlotsComponent, AugmentModuleInsertedEvent>(OnModuleInserted);
        SubscribeLocalEvent<AugmentModuleSlotsComponent, AugmentModuleRemovedEvent>(OnModuleRemoved);

        SubscribeLocalEvent<AugmentUniversalModuleAccumulatorComponent, CollectAugmentNeuroInterfaceMetricsEvent>(OnCollectMetrics);
    }

    private void OnSlotsStartup(Entity<AugmentModuleSlotsComponent> ent, ref ComponentStartup args)
    {
        RecalculateAccumulator(ent);
    }

    private void OnModuleInserted(Entity<AugmentModuleSlotsComponent> ent, ref AugmentModuleInsertedEvent args)
    {
        RecalculateAccumulator(ent);

        if (args.Body is { } body)
            UpdateBodyDraw(body);
    }

    private void OnModuleRemoved(Entity<AugmentModuleSlotsComponent> ent, ref AugmentModuleRemovedEvent args)
    {
        RecalculateAccumulator(ent);

        if (args.Body is { } body)
            UpdateBodyDraw(body);
    }

    private void RecalculateAccumulator(Entity<AugmentModuleSlotsComponent> ent)
    {
        if (!TryComp<ItemSlotsComponent>(ent, out var itemSlots))
        {
            RemComp<AugmentUniversalModuleAccumulatorComponent>(ent);
            return;
        }

        var maxDelta = 0f;
        var currentDelta = 0f;
        var powerDelta = 0f;
        var visionActivePowerMultiplier = 1f;
        var visionActivePowerDelta = 0f;
        var visionActiveNeuroMultiplier = 1f;
        var visionActiveNeuroDelta = 0f;
        var itemPanelActivePowerMultiplier = 1f;
        var itemPanelActivePowerDelta = 0f;
        var itemPanelActiveNeuroMultiplier = 1f;
        var itemPanelActiveNeuroDelta = 0f;
        var neuroTooltip = "neuro-interface-tooltip-source-neuro-module-passive";
        var powerTooltip = "neuro-interface-tooltip-source-power-module-passive";

        foreach (var definition in ent.Comp.Slots)
        {
            if (!_itemSlots.TryGetSlot(ent, definition.Id, out var slot, itemSlots))
                continue;

            if (slot.Item is not { } moduleUid)
                continue;

            if (!TryComp<AugmentUniversalModuleComponent>(moduleUid, out var module))
                continue;

            maxDelta += module.MaxNeuroLoadDelta;
            currentDelta += module.CurrentNeuroLoadDelta;
            powerDelta += module.PassivePowerDrawDelta;
            visionActivePowerMultiplier *= module.VisionActivePowerMultiplier;
            visionActivePowerDelta += module.VisionActivePowerDelta;
            visionActiveNeuroMultiplier *= module.VisionActiveNeuroMultiplier;
            visionActiveNeuroDelta += module.VisionActiveNeuroDelta;
            itemPanelActivePowerMultiplier *= module.ItemPanelActivePowerMultiplier;
            itemPanelActivePowerDelta += module.ItemPanelActivePowerDelta;
            itemPanelActiveNeuroMultiplier *= module.ItemPanelActiveNeuroMultiplier;
            itemPanelActiveNeuroDelta += module.ItemPanelActiveNeuroDelta;

            neuroTooltip = module.NeuroLoadTooltipSource;
            powerTooltip = module.PowerTooltipSource;
        }

        if (maxDelta == 0f
            && currentDelta == 0f
            && powerDelta == 0f
            && visionActivePowerMultiplier == 1f
            && visionActivePowerDelta == 0f
            && visionActiveNeuroMultiplier == 1f
            && visionActiveNeuroDelta == 0f
            && itemPanelActivePowerMultiplier == 1f
            && itemPanelActivePowerDelta == 0f
            && itemPanelActiveNeuroMultiplier == 1f
            && itemPanelActiveNeuroDelta == 0f)
        {
            RemComp<AugmentUniversalModuleAccumulatorComponent>(ent);
            return;
        }

        var accumulator = EnsureComp<AugmentUniversalModuleAccumulatorComponent>(ent);
        accumulator.MaxNeuroLoadDelta = maxDelta;
        accumulator.CurrentNeuroLoadDelta = currentDelta;
        accumulator.PassivePowerDrawDelta = powerDelta;
        accumulator.VisionActivePowerMultiplier = visionActivePowerMultiplier;
        accumulator.VisionActivePowerDelta = visionActivePowerDelta;
        accumulator.VisionActiveNeuroMultiplier = visionActiveNeuroMultiplier;
        accumulator.VisionActiveNeuroDelta = visionActiveNeuroDelta;
        accumulator.ItemPanelActivePowerMultiplier = itemPanelActivePowerMultiplier;
        accumulator.ItemPanelActivePowerDelta = itemPanelActivePowerDelta;
        accumulator.ItemPanelActiveNeuroMultiplier = itemPanelActiveNeuroMultiplier;
        accumulator.ItemPanelActiveNeuroDelta = itemPanelActiveNeuroDelta;
        accumulator.NeuroLoadTooltipSource = neuroTooltip;
        accumulator.PowerTooltipSource = powerTooltip;
    }

    private void OnCollectMetrics(Entity<AugmentUniversalModuleAccumulatorComponent> ent, ref CollectAugmentNeuroInterfaceMetricsEvent args)
    {
        if (ent.Comp.CurrentNeuroLoadDelta != 0f)
        {
            args.PassiveNeuroLoadEntries.Add(new NeuroInterfaceMetricEntry(
                ent.Comp.NeuroLoadTooltipSource,
                ent.Comp.CurrentNeuroLoadDelta));
        }

        if (args.PowerEnabled && ent.Comp.PassivePowerDrawDelta != 0f)
        {
            args.PassivePowerEntries.Add(new NeuroInterfaceMetricEntry(
                ent.Comp.PowerTooltipSource,
                ent.Comp.PassivePowerDrawDelta));
        }

        if (args.PowerEnabled)
        {
            if (ent.Comp.VisionActivePowerMultiplier != 1f)
            {
                args.ActivePowerEntries.Add(new NeuroInterfaceMetricEntry(
                    "neuro-interface-tooltip-source-power-module-vision-active-multiplier",
                    ent.Comp.VisionActivePowerMultiplier));
            }

            if (ent.Comp.VisionActivePowerDelta != 0f)
            {
                args.ActivePowerEntries.Add(new NeuroInterfaceMetricEntry(
                    "neuro-interface-tooltip-source-power-module-vision-active-delta",
                    ent.Comp.VisionActivePowerDelta));
            }

            if (ent.Comp.ItemPanelActivePowerMultiplier != 1f)
            {
                args.ActivePowerEntries.Add(new NeuroInterfaceMetricEntry(
                    "neuro-interface-tooltip-source-power-module-itempanel-active-multiplier",
                    ent.Comp.ItemPanelActivePowerMultiplier));
            }

            if (ent.Comp.ItemPanelActivePowerDelta != 0f)
            {
                args.ActivePowerEntries.Add(new NeuroInterfaceMetricEntry(
                    "neuro-interface-tooltip-source-power-module-itempanel-active-delta",
                    ent.Comp.ItemPanelActivePowerDelta));
            }
        }

        if (ent.Comp.VisionActiveNeuroMultiplier != 1f)
        {
            args.ActiveNeuroLoadEntries.Add(new NeuroInterfaceMetricEntry(
                "neuro-interface-tooltip-source-neuro-module-vision-active-multiplier",
                ent.Comp.VisionActiveNeuroMultiplier));
        }

        if (ent.Comp.VisionActiveNeuroDelta != 0f)
        {
            args.ActiveNeuroLoadEntries.Add(new NeuroInterfaceMetricEntry(
                "neuro-interface-tooltip-source-neuro-module-vision-active-delta",
                ent.Comp.VisionActiveNeuroDelta));
        }

        if (ent.Comp.ItemPanelActiveNeuroMultiplier != 1f)
        {
            args.ActiveNeuroLoadEntries.Add(new NeuroInterfaceMetricEntry(
                "neuro-interface-tooltip-source-neuro-module-itempanel-active-multiplier",
                ent.Comp.ItemPanelActiveNeuroMultiplier));
        }

        if (ent.Comp.ItemPanelActiveNeuroDelta != 0f)
        {
            args.ActiveNeuroLoadEntries.Add(new NeuroInterfaceMetricEntry(
                "neuro-interface-tooltip-source-neuro-module-itempanel-active-delta",
                ent.Comp.ItemPanelActiveNeuroDelta));
        }
    }

    private void UpdateBodyDraw(EntityUid body)
    {
        if (_augmentPower.GetBodyAugment(body) is { } slot)
            _augmentPower.UpdateDrawRate(slot.Owner);
    }
}
