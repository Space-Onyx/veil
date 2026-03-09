using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.Surgery.Augments;

public sealed partial class AugmentHoloPdaOpenEvent : InstantActionEvent;

public sealed partial class AugmentHoloPdaMedTekScanEvent : EntityTargetActionEvent;

[Serializable, NetSerializable]
public sealed partial class AugmentHoloPdaEjectIdDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class AugmentHoloPdaEjectCartridgeDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class AugmentHoloPdaReplaceIdDoAfterEvent : DoAfterEvent
{
    [DataField]
    public NetEntity NewIdCard;

    public override DoAfterEvent Clone()
    {
        return new AugmentHoloPdaReplaceIdDoAfterEvent { NewIdCard = NewIdCard };
    }
}
