using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.ZLevels;

[Serializable, NetSerializable]
public sealed partial class ZCeilingPryDoAfterEvent : SimpleDoAfterEvent
{
    public NetEntity CeilingGrid;
    public Vector2i CeilingTilePos;
    public ZCeilingPryDoAfterEvent(NetEntity ceilingGrid, Vector2i ceilingTilePos)
    {
        CeilingGrid = ceilingGrid;
        CeilingTilePos = ceilingTilePos;
    }
}
