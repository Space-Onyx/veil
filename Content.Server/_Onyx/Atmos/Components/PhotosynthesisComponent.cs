using Robust.Shared.GameObjects;

namespace Content.Server._Onyx.Atmos.Components;

[RegisterComponent]
public sealed partial class PhotosynthesisComponent : Component
{
    [DataField]
    public float UpdateDelay = 5f;

    [DataField]
    public float MolesPerSecond = 0.002f;

    [ViewVariables]
    public float AccumulatedFrametime;
}
