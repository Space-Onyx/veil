using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CyberDeckScriptOpticsOverloadComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Range = 7f;

    [DataField, AutoNetworkedField]
    public float RangeWithoutOptics = 6f;

    [DataField, AutoNetworkedField]
    public float OperationDelay = 1f;

    [DataField, AutoNetworkedField]
    public float TargetSearchRadius = 1.2f;

    [DataField]
    public float MinDisableDuration = 5f;

    [DataField]
    public float MaxDisableDuration = 6f;

    [DataField, AutoNetworkedField]
    public Color OverlayFillColor = new(255, 52, 134, 52);

    [DataField, AutoNetworkedField]
    public Color OverlayOuterOutlineColor = new(0, 0, 0, 230);

    [DataField, AutoNetworkedField]
    public Color OverlayInnerOutlineColor = new(255, 52, 134, 245);
}

[Serializable, NetSerializable]
public sealed partial class CyberDeckScriptOpticsOverloadDoAfterEvent : DoAfterEvent
{
    [DataField]
    public NetEntity Target;

    [DataField]
    public NetEntity Body;

    public override DoAfterEvent Clone()
    {
        return new CyberDeckScriptOpticsOverloadDoAfterEvent
        {
            Target = Target,
            Body = Body,
        };
    }
}
