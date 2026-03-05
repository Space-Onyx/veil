using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AugmentItemPanelComponent : Component
{
    [DataField(required: true)]
    public EntProtoId ItemPrototype = string.Empty;

    [DataField]
    public SpriteSpecifier? Icon;

    [DataField, AutoNetworkedField]
    public EntityUid? SpawnedItem;

    [DataField, AutoNetworkedField]
    public bool IsEquipped = false;

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class AugmentItemPanelActiveItemComponent : Component
{
    [DataField]
    public EntityUid AugmentEntity;
}
