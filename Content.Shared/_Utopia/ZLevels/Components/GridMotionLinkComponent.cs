using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._Utopia.ZLevels.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GridMotionLinkComponent : Component
{
    [DataField, AutoNetworkedField]
    public string GroupId = string.Empty;

    [DataField, AutoNetworkedField]
    public Vector2 Offset = Vector2.Zero;

    // <Onyx-Tweak>
    [DataField, AutoNetworkedField]
    public bool AutoCalculateOffset = true;
    // <Onyx-Tweak>

    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public EntityUid Root;
}
