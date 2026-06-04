using Content.Goobstation.Maths.FixedPoint;

namespace Content.Shared._Onyx.Clothing;

[RegisterComponent, Access(typeof(ClothingDirtSystem))]
public sealed partial class ClothingDirtableComponent : Component
{
    [DataField]
    public string Solution = ClothingDirtSystem.DefaultSolutionName;

    [DataField]
    public FixedPoint2 Capacity = FixedPoint2.New(10);

    [DataField]
    public FixedPoint2 MaxReagentAmount = FixedPoint2.New(2);

    [DataField]
    public FixedPoint2 DryMinimum = FixedPoint2.New(0.5f);

    [DataField]
    public FixedPoint2 DryAmount = FixedPoint2.New(0.5f);

    [DataField]
    public float DryInterval = 5f;

    public float DryAccumulator;

    public bool DryingActive;
}
