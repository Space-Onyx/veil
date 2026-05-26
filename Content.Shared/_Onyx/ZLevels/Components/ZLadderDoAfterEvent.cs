using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.ZLevels.Components;

[Serializable, NetSerializable]
public sealed partial class ZLadderDoAfterEvent : DoAfterEvent
{
    public NetEntity? PulledEntity;

    private ZLadderDoAfterEvent()
    {
    }

    public ZLadderDoAfterEvent(NetEntity? pulledEntity)
    {
        PulledEntity = pulledEntity;
    }

    public override DoAfterEvent Clone()
    {
        return new ZLadderDoAfterEvent(PulledEntity);
    }
}
