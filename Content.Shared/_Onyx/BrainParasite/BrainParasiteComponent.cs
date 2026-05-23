using Content.Shared.Actions.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._Onyx.BrainParasite;

[RegisterComponent]
public sealed partial class BrainParasiteComponent : Component
{
    [DataField]
    public EntProtoId<EntityTargetActionComponent> EnterHostAction = "ActionBrainParasiteEnterHost";

    [DataField]
    public string OrganSlot = "prefrontalCortex";

    [DataField]
    public EntityUid? EnterHostActionEntity;

    [DataField]
    public EntityUid? Host;
}
