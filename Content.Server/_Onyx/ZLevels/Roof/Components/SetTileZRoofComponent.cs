namespace Content.Server._Onyx.ZLevels.Roof.Components;

[RegisterComponent]
public sealed partial class SetTileZRoofComponent : Component
{
    [DataField(required: true)]
    public bool Value;
}
