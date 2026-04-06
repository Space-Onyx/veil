using System.Collections.Generic;
using Robust.Shared.GameStates;
using Robust.Shared.Maths;

namespace Content.Shared._Onyx.ZLevels.Roof.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TileZRoofComponent : Component
{
    public const int ChunkSize = 8;

    [DataField, AutoNetworkedField]
    public Dictionary<Vector2i, ulong> DisableData = new();

    [DataField, AutoNetworkedField]
    public Dictionary<Vector2i, ulong> EnableData = new();
}
