using Content.Shared.Tag;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AugmentCompatibilityComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool AllowAugments = true;

    [DataField, AutoNetworkedField]
    public int MaxAugmentCount = -1;

    [DataField, AutoNetworkedField]
    public HashSet<ProtoId<TagPrototype>> AllowedAugmentTags = new();

    [DataField, AutoNetworkedField]
    public HashSet<ProtoId<TagPrototype>> BlockedAugmentTags = new();
}
