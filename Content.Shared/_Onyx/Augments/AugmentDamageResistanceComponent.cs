using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Augments;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AugmentDamageResistanceComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public string DamageModifierSetId = string.Empty;
}
