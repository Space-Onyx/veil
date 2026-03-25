using Content.Shared.DoAfter;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent]
public sealed partial class CyberDeckScriptMotorImpairmentComponent : Component
{
    [DataField]
    public float Range = 7f;

    [DataField]
    public float OperationDelay = 1f;

    [DataField]
    public float TargetSearchRadius = 1.2f;

    [DataField]
    public float MinDisableDuration = 4f;

    [DataField]
    public float MaxDisableDuration = 5f;

    [DataField]
    public Color OverlayFillColor = new(255, 124, 34, 58);

    [DataField]
    public Color OverlayOuterOutlineColor = new(0, 0, 0, 230);

    [DataField]
    public Color OverlayInnerOutlineColor = new(255, 160, 52, 245);
}

[Serializable, NetSerializable]
public sealed partial class CyberDeckScriptMotorImpairmentDoAfterEvent : DoAfterEvent
{
    [DataField]
    public NetEntity Target;

    [DataField]
    public NetEntity Body;

    public override DoAfterEvent Clone()
    {
        return new CyberDeckScriptMotorImpairmentDoAfterEvent
        {
            Target = Target,
            Body = Body,
        };
    }
}
