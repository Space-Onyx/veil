namespace Content.Server._CE.ZLevels.Roof.Components;

[RegisterComponent]
public sealed partial class SetTileZRoofComponent : Component
{
    [DataField(required: true)]
    public bool Value;
}
