namespace Content.Server._Onyx.Weather.Components;

[RegisterComponent]
public sealed partial class SetTileWeatherComponent : Component
{
    [DataField(required: true)]
    public bool Disable;
}
