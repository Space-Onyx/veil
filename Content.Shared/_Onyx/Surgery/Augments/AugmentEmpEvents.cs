namespace Content.Shared._Onyx.Surgery.Augments;

[ByRefEvent]
public record struct AugmentEmpDisabledEvent(EntityUid Body);

[ByRefEvent]
public record struct AugmentEmpRestoredEvent(EntityUid Body);

[ByRefEvent]
public record struct AugmentManuallyDisabledEvent(EntityUid Body);

[ByRefEvent]
public record struct AugmentManuallyRestoredEvent(EntityUid Body);
