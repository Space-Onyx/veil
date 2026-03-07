namespace Content.Shared._Onyx.Surgery.Augments;

[ByRefEvent]
public record struct AugmentEmpDisabledEvent(EntityUid Body);

[ByRefEvent]
public record struct AugmentEmpRestoredEvent(EntityUid Body);
