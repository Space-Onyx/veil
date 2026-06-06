using Content.Goobstation.Maths.FixedPoint;

namespace Content.Shared._Onyx.Clothing;

[RegisterComponent]
public sealed partial class ClothingDirtWasherComponent : Component
{
    [DataField]
    public string CleanerReagent = "Water";

    [DataField]
    public FixedPoint2 Amount = FixedPoint2.New(1);
}
