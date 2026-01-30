namespace Content.Server._Vortex.Weather.Components;

[RegisterComponent]
public sealed partial class SetTileWeatherComponent : Component
{
    [DataField(required: true)]
    public bool Disable;
}
