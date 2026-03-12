using System.Collections.Generic;
using Content.Goobstation.Shared.Augments;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Body.Events;
using Content.Shared.Containers.ItemSlots;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentUniversalModuleSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedAugmentPowerCellSystem _augmentPower = default!;

    private readonly HashSet<EntityUid> _dirtyHosts = new();
    private readonly List<EntityUid> _dirtyBuffer = new();
    private readonly List<EntityUid> _moduleSources = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentModuleSlotsComponent, ComponentStartup>(OnSlotsStartup);
        SubscribeLocalEvent<AugmentModuleSlotsComponent, AugmentModuleInsertedEvent>(OnModuleInserted);
        SubscribeLocalEvent<AugmentModuleSlotsComponent, AugmentModuleRemovedEvent>(OnModuleRemoved);
        SubscribeLocalEvent<AugmentModuleSlotsComponent, AugmentEmpDisabledEvent>(OnEmpDisabled);
        SubscribeLocalEvent<AugmentModuleSlotsComponent, AugmentEmpRestoredEvent>(OnEmpRestored);
        SubscribeLocalEvent<AugmentModuleSlotsComponent, AugmentManuallyDisabledEvent>(OnManuallyDisabled);
        SubscribeLocalEvent<AugmentModuleSlotsComponent, AugmentManuallyRestoredEvent>(OnManuallyRestored);
        SubscribeLocalEvent<AugmentModuleSlotsComponent, AugmentLostPowerEvent>(OnLostPower);
        SubscribeLocalEvent<AugmentModuleSlotsComponent, AugmentGainedPowerEvent>(OnGainedPower);

        SubscribeLocalEvent<AugmentUniversalModuleAccumulatorComponent, CollectAugmentNeuroInterfaceMetricsEvent>(OnCollectMetrics);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_dirtyHosts.Count == 0)
            return;

        _dirtyBuffer.Clear();
        foreach (var hostUid in _dirtyHosts)
        {
            _dirtyBuffer.Add(hostUid);
        }

        _dirtyHosts.Clear();

        foreach (var hostUid in _dirtyBuffer)
        {
            if (!TryComp<AugmentModuleSlotsComponent>(hostUid, out var slots))
                continue;

            RecalculateAccumulator((hostUid, slots));
        }
    }

    private void OnSlotsStartup(Entity<AugmentModuleSlotsComponent> ent, ref ComponentStartup args)
    {
        HandleSlotsChanged(ent, null);
    }

    private void OnModuleInserted(Entity<AugmentModuleSlotsComponent> ent, ref AugmentModuleInsertedEvent args)
    {
        HandleSlotsChanged(ent, args.Body);
    }

    private void OnModuleRemoved(Entity<AugmentModuleSlotsComponent> ent, ref AugmentModuleRemovedEvent args)
    {
        HandleSlotsChanged(ent, args.Body);
    }

    private void OnEmpDisabled(Entity<AugmentModuleSlotsComponent> ent, ref AugmentEmpDisabledEvent args)
    {
        HandleHostStateInvalidated(ent, args.Body);
    }

    private void OnEmpRestored(Entity<AugmentModuleSlotsComponent> ent, ref AugmentEmpRestoredEvent args)
    {
        HandleHostStateInvalidated(ent, args.Body);
    }

    private void OnManuallyDisabled(Entity<AugmentModuleSlotsComponent> ent, ref AugmentManuallyDisabledEvent args)
    {
        HandleHostStateInvalidated(ent, args.Body);
    }

    private void OnManuallyRestored(Entity<AugmentModuleSlotsComponent> ent, ref AugmentManuallyRestoredEvent args)
    {
        HandleHostStateInvalidated(ent, args.Body);
    }

    private void OnLostPower(Entity<AugmentModuleSlotsComponent> ent, ref AugmentLostPowerEvent args)
    {
        HandleHostStateInvalidated(ent, args.Body);
    }

    private void OnGainedPower(Entity<AugmentModuleSlotsComponent> ent, ref AugmentGainedPowerEvent args)
    {
        HandleHostStateInvalidated(ent, args.Body);
    }

    private void RecalculateAccumulator(Entity<AugmentModuleSlotsComponent> ent)
    {
        if (!TryComp<ItemSlotsComponent>(ent, out var itemSlotsComp))
        {
            RemComp<AugmentUniversalModuleAccumulatorComponent>(ent);
            return;
        }

        AugmentEffectPipeline.CollectUniversalModuleSources(ent, _itemSlots, itemSlotsComp, _moduleSources);
        var aggregate = AugmentEffectPipeline.AggregateUniversalModules(EntityManager, _moduleSources);

        if (aggregate.IsNeutral())
        {
            RemComp<AugmentUniversalModuleAccumulatorComponent>(ent);
            return;
        }

        var accumulator = EnsureComp<AugmentUniversalModuleAccumulatorComponent>(ent);
        aggregate.ApplyTo(accumulator);
        accumulator.Dirty = false;
        accumulator.Revision++;
    }

    private void OnCollectMetrics(Entity<AugmentUniversalModuleAccumulatorComponent> ent, ref CollectAugmentNeuroInterfaceMetricsEvent args)
    {
        if (ent.Comp.Dirty && TryComp<AugmentModuleSlotsComponent>(ent.Owner, out var slots))
        {
            RecalculateAccumulator((ent.Owner, slots));
            _dirtyHosts.Remove(ent.Owner);
        }

        if (!TryComp<AugmentUniversalModuleAccumulatorComponent>(ent.Owner, out var cache))
            return;

        AddMetricIfNonZero(args.PassiveNeuroLoadEntries, cache.NeuroLoadTooltipSource, cache.CurrentNeuroLoad);

        if (args.PowerEnabled)
            AddMetricIfNonZero(args.PassivePowerEntries, cache.PowerTooltipSource, cache.PassivePowerDraw);

        if (args.PowerEnabled)
        {
            AddMetricIfNotOne(args.ActivePowerEntries, "neuro-interface-tooltip-source-power-module-vision-active-multiplier", cache.VisionActivePowerMultiplier);
            AddMetricIfNonZero(args.ActivePowerEntries, "neuro-interface-tooltip-source-power-module-vision-active-delta", cache.VisionActivePower);
            AddMetricIfNotOne(args.ActivePowerEntries, "neuro-interface-tooltip-source-power-module-itempanel-active-multiplier", cache.ItemPanelActivePowerMultiplier);
            AddMetricIfNonZero(args.ActivePowerEntries, "neuro-interface-tooltip-source-power-module-itempanel-active-delta", cache.ItemPanelActivePower);
        }

        AddMetricIfNotOne(args.ActiveNeuroLoadEntries, "neuro-interface-tooltip-source-neuro-module-vision-active-multiplier", cache.VisionActiveNeuroMultiplier);
        AddMetricIfNonZero(args.ActiveNeuroLoadEntries, "neuro-interface-tooltip-source-neuro-module-vision-active-delta", cache.VisionActiveNeuro);
        AddMetricIfNotOne(args.ActiveNeuroLoadEntries, "neuro-interface-tooltip-source-neuro-module-itempanel-active-multiplier", cache.ItemPanelActiveNeuroMultiplier);
        AddMetricIfNonZero(args.ActiveNeuroLoadEntries, "neuro-interface-tooltip-source-neuro-module-itempanel-active-delta", cache.ItemPanelActiveNeuro);
    }

    private void MarkCacheDirty(EntityUid hostUid)
    {
        if (TryComp<AugmentUniversalModuleAccumulatorComponent>(hostUid, out var cache))
            cache.Dirty = true;

        _dirtyHosts.Add(hostUid);
    }

    private void UpdateBodyDraw(EntityUid body)
    {
        if (_augmentPower.GetBodyAugment(body) is { } slot)
            _augmentPower.UpdateDrawRate(slot.Owner);
    }

    private void HandleSlotsChanged(Entity<AugmentModuleSlotsComponent> ent, EntityUid? body)
    {
        MarkCacheDirty(ent.Owner);
        RecalculateAccumulator(ent);
        _dirtyHosts.Remove(ent.Owner);

        if (body is { } bodyUid)
            UpdateBodyDraw(bodyUid);
    }

    private void HandleHostStateInvalidated(Entity<AugmentModuleSlotsComponent> ent, EntityUid? body)
    {
        MarkCacheDirty(ent.Owner);

        if (body is { } bodyUid)
            UpdateBodyDraw(bodyUid);
    }

    private static void AddMetricIfNonZero(List<NeuroInterfaceMetricEntry> target, string locKey, float value)
    {
        if (value == 0f)
            return;

        target.Add(new NeuroInterfaceMetricEntry(locKey, value));
    }

    private static void AddMetricIfNotOne(List<NeuroInterfaceMetricEntry> target, string locKey, float value)
    {
        if (value == 1f)
            return;

        target.Add(new NeuroInterfaceMetricEntry(locKey, value));
    }
}
