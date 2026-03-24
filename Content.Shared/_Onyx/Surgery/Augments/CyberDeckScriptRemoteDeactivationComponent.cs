using System.Collections.Generic;
using Content.Shared.Access;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CyberDeckScriptRemoteDeactivationComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Range = 10f;

    [DataField, AutoNetworkedField]
    public float OperationDelay = 2f;

    [DataField, AutoNetworkedField]
    public float TargetSearchRadius = 1.2f;

    [DataField("access"), AutoNetworkedField]
    public List<ProtoId<AccessLevelPrototype>> Access = new();

    [DataField("inverted"), AutoNetworkedField]
    public bool Inverted;

    [DataField, AutoNetworkedField]
    public Color OverlayFillColor = new(24, 132, 255, 26);

    [DataField, AutoNetworkedField]
    public Color OverlayOuterOutlineColor = new(0, 0, 0, 230);

    [DataField, AutoNetworkedField]
    public Color OverlayInnerOutlineColor = new(24, 132, 255, 245);
}

[Serializable, NetSerializable]
public sealed partial class CyberDeckScriptRemoteDeactivationDoAfterEvent : DoAfterEvent
{
    [DataField]
    public NetEntity Target;

    [DataField]
    public NetEntity Body;

    public override DoAfterEvent Clone()
    {
        return new CyberDeckScriptRemoteDeactivationDoAfterEvent
        {
            Target = Target,
            Body = Body,
        };
    }
}
