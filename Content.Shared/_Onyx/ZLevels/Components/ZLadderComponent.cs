using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.ZLevels.Components;

[Serializable, NetSerializable]
public enum ZLadderDirection : byte
{
    Up,
    Down,
}

[RegisterComponent]
public sealed partial class ZLadderComponent : Component
{
    [DataField]
    public ZLadderDirection Direction = ZLadderDirection.Up;
}
