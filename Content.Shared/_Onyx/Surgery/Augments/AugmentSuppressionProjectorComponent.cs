using System;
using System.Collections.Generic;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.Surgery.Augments;

[Serializable, NetSerializable]
public enum AugmentSuppressionFieldShape : byte
{
    Circle = 0,
    Square = 1,
}

[RegisterComponent]
public sealed partial class AugmentSuppressionProjectorComponent : Component
{
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool Enabled = true;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float Radius = 4f;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public AugmentSuppressionFieldShape Shape = AugmentSuppressionFieldShape.Circle;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public HashSet<ProtoId<TagPrototype>> TargetTags = new();

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public bool InvertTags;

    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public float UpdateInterval = 0.5f;

    [ViewVariables]
    public TimeSpan NextUpdateAt = TimeSpan.Zero;

    [ViewVariables]
    public HashSet<EntityUid> AffectedBodies = new();
}

[RegisterComponent]
public sealed partial class AugmentSuppressedByProjectorsComponent : Component
{
    [ViewVariables]
    public HashSet<EntityUid> Sources = new();

    [ViewVariables]
    public bool CreatedEmpDisabledComponent;
}
