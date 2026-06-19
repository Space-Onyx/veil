namespace Content.Server._DV.Weather;

public sealed partial class WeatherSchedulerComponent
{
    /// <summary>
    /// Select stages randomly by their weights instead of running them sequentially.
    /// </summary>
    [DataField]
    public bool Random;
}
