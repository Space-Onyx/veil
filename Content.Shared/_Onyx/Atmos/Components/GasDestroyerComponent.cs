using Content.Shared.Atmos;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.Atmos.Components;

[NetworkedComponent]
[AutoGenerateComponentState]
[RegisterComponent]
public sealed partial class GasDestroyerComponent : Component
{
    /// <summary>
    ///     Operational state of the destroyer.
    /// </summary>
    [AutoNetworkedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public GasDestroyerState DestroyerState = GasDestroyerState.Disabled;

    /// <summary>
    ///     If the number of moles in the external environment is at or below this number, no gas will be destroyed.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public float MinExternalAmount;

    /// <summary>
    ///     If the pressure (in kPa) of the external environment is at or below this number, no gas will be destroyed.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public float MinExternalPressure;

    /// <summary>
    ///     Gas to destroy.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public Gas? DestroyGas;

    /// <summary>
    ///     List of gases and their coefficient to destroy. Will use this list instead of <see cref="DestroyGas"/>.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public Dictionary<Gas, float>? ListDestroyGas;

    /// <summary>
    ///     If true, destroys all gases proportionally instead of using configured gas targets.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public bool DestroyAnyGas;

    /// <summary>
    ///     Number of moles destroyed per second when the destroyer is working.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField]
    public float DestroyAmount = Atmospherics.MolesCellStandard * 20f;
}

[Serializable, NetSerializable]
public enum GasDestroyerState : byte
{
    Disabled,
    Idle,
    Working,
}
