using Content.Goobstation.Maths.FixedPoint;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Onyx.EntityEffects.Effects;

public sealed partial class RestoreOrganIntegrity : EventEntityEffect<RestoreOrganIntegrity>
{
    [DataField(required: true, customTypeSerializer: typeof(ComponentNameSerializer))]
    public string Organ = string.Empty;

    [DataField]
    public FixedPoint2 PercentPerUnit = FixedPoint2.New(0.01);

    [DataField]
    public string Identifier = "RestoreOrganIntegrity";

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-onyx-restore-organ-integrity",
            ("percent", PercentPerUnit * 100),
            ("organ", Organ));
}
