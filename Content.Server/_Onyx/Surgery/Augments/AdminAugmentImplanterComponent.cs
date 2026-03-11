using System.Collections.Generic;

namespace Content.Server._Onyx.Surgery.Augments;

[RegisterComponent]
public sealed partial class AdminAugmentImplanterComponent : Component
{
    [DataField]
    public List<PresetAugmentEntry> Entries = new();

    [DataField]
    public bool ReplaceExisting = true;

    [DataField]
    public bool OneTimeUse;

    [DataField]
    public bool Used;
}
