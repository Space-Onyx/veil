using System.Collections.Generic;

namespace Content.Shared._Onyx.Surgery.Augments;

[ByRefEvent]
public record struct CollectAugmentNeuroInterfaceMetricsEvent(
    bool PowerEnabled,
    List<NeuroInterfaceMetricEntry> PassivePowerEntries,
    List<NeuroInterfaceMetricEntry> ActivePowerEntries,
    List<NeuroInterfaceMetricEntry> PassiveNeuroLoadEntries,
    List<NeuroInterfaceMetricEntry> ActiveNeuroLoadEntries);
