using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.ProxyControl;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ProxyControlComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Target;

    [DataField, AutoNetworkedField]
    public bool RelayCamera = true;

    [DataField, AutoNetworkedField]
    public bool RelayMovement;

    [DataField, AutoNetworkedField]
    public bool RelayInteraction;

    [DataField, AutoNetworkedField]
    public bool RelayHands = true;

    [DataField, AutoNetworkedField]
    public bool RelayInventory = true;

    [DataField, AutoNetworkedField]
    public bool RelayActions = true;

    [DataField, AutoNetworkedField]
    public bool RelaySpeech = true;

    [DataField]
    public TimeSpan NextProxyAction;

    [DataField]
    public TimeSpan ProxyCooldown = TimeSpan.FromSeconds(0.5);

    public bool IsLinked => Target != null;

    public bool HadEyeComponent;
    public bool CapturedEyeState;
    public bool HadPreviousEyeTarget;
    public EntityUid? PreviousEyeTarget;
    public bool CapturedMovementRelayState;
    public bool HadMovementRelay;
    public EntityUid PreviousMovementRelay;
    public bool CapturedInteractionRelayState;
    public bool HadInteractionRelay;
    public EntityUid? PreviousInteractionRelay;
    public bool CreatedInteractionRelay;
}
