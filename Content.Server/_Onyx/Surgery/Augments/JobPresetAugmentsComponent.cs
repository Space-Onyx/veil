using Robust.Shared.Prototypes;

namespace Content.Server._Onyx.Surgery.Augments;

[RegisterComponent]
public sealed partial class JobPresetAugmentsComponent : Component
{
    [DataField]
    public List<PresetAugmentEntry> Entries = new();

    [DataField]
    public List<EntProtoId> Augments = new();
}
