using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent]
public sealed partial class AugmentInterceptComponent : Component
{
    [DataField]
    public EntityUid? InterceptActionEntity;

    [DataField]
    public EntityUid? ReconnectActionEntity;

    [DataField]
    public EntityUid? LastInterceptedBody;

    [DataField]
    public EntityUid? LastInterceptedInterface;

    [DataField]
    public float ForeignManipulationNeuroLoad = 2f;

    [DataField]
    public float ForeignManipulationPowerDraw = 1f;

    [DataField]
    public float ForeignManipulationDurationSeconds = 30f;
}
