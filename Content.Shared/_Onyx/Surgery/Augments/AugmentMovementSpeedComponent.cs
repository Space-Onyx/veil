using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.Surgery.Augments;

[Serializable, NetSerializable]
public enum SpeedModifierType : byte
{
    Percentage,
    Flat,
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AugmentMovementSpeedComponent : Component
{
    [DataField, AutoNetworkedField]
    public SpeedModifierType ModifierType = SpeedModifierType.Percentage;

    [DataField, AutoNetworkedField]
    public float WalkMultiplier = 1.0f;

    [DataField, AutoNetworkedField]
    public float SprintMultiplier = 1.0f;

    [DataField, AutoNetworkedField]
    public float WalkAddition = 0f;

    [DataField, AutoNetworkedField]
    public float SprintAddition = 0f;

    [DataField, AutoNetworkedField]
    public string? AugmentId;

    [DataField, AutoNetworkedField]
    public float PowerDraw;

    [DataField, AutoNetworkedField]
    public bool RequiresPower = true;
}
