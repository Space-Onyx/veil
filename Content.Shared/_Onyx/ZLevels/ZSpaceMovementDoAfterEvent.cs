using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.ZLevels;

[Serializable, NetSerializable]
public sealed partial class ZSpaceMoveUpDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class ZSpaceMoveDownDoAfterEvent : SimpleDoAfterEvent;

public sealed partial class ZSpaceMoveUpAction : InstantActionEvent;

public sealed partial class ZSpaceMoveDownAction : InstantActionEvent;
