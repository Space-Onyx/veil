namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent]
public sealed partial class AugmentBehaviorPolicyComponent : Component
{
    [DataField]
    public bool CanToggle = true;

    [DataField]
    public bool AffectedByBrainDeactivation = true;

    [DataField]
    public bool AffectedByEmp = true;

    [DataField]
    public bool AffectedBySuppression = true;
}
