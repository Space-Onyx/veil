namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent]
public sealed partial class CyberDeckScriptImplantFailureComponent : Component
{
    [DataField]
    public float Range = 7f;

    [DataField]
    public float MinDisableDuration = 6f;

    [DataField]
    public float MaxDisableDuration = 8f;

    [DataField]
    public bool AffectSelf = false;
}
