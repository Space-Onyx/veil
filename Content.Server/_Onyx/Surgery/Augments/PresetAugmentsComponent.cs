using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared.Body.Part;

namespace Content.Server._Onyx.Surgery.Augments;

[RegisterComponent]
public sealed partial class PresetAugmentsComponent : Component
{
    [DataField]
    public List<PresetAugmentEntry> Entries = new();

    [DataField]
    public List<EntProtoId> Augments = new();
}

[DataRecord]
public sealed partial class PresetAugmentEntry
{
    [DataField(required: true)]
    public EntProtoId Prototype;

    [DataField(required: true)]
    public string Slot = string.Empty;

    [DataField]
    public BodyPartType PartType = BodyPartType.Other;

    [DataField]
    public BodyPartSymmetry Symmetry = BodyPartSymmetry.None;
}
