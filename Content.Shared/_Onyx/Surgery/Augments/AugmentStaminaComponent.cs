using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.Surgery.Augments;

[Serializable, NetSerializable]
public enum StaminaModifierType : byte
{
    Percentage,
    Flat,
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AugmentStaminaComponent : Component
{
    [DataField, AutoNetworkedField]
    public StaminaModifierType ModifierType = StaminaModifierType.Percentage;

    [DataField, AutoNetworkedField]
    public float StaminaMultiplier = 1.0f;

    [DataField, AutoNetworkedField]
    public float StaminaAddition = 0f;

    [DataField, AutoNetworkedField]
    public float RecoveryMultiplier = 1.0f;
}
