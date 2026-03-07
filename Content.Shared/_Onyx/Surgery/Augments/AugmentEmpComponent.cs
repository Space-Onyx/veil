using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AugmentEmpComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool EmpVulnerable = true;

    [DataField, AutoNetworkedField]
    public float MinDisableDuration = 5f;

    [DataField, AutoNetworkedField]
    public float MaxDisableDuration = 15f;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class AugmentEmpDisabledComponent : Component
{
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan DisabledUntil;
}
