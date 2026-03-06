using Content.Shared.Tag;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Onyx.Surgery.Conditions;

[RegisterComponent, NetworkedComponent]
public sealed partial class SurgeryOrganTagConditionComponent : Component
{
    [DataField(required: true)]
    public ProtoId<TagPrototype> Tag;

    [DataField]
    public bool Inverse;

    [DataField]
    public bool Reattaching;
}
