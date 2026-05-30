using Robust.Shared.Configuration;

namespace Content.Shared._Mono.CCVar;

/// <summary>
/// Contains CVars used by Mono.
/// </summary>
[CVarDefs]
public sealed partial class MonoCVars
{
    #region Detection

    /// <summary>
    ///     Multiplier of grid thermal detection radius.
    /// </summary>
    public static readonly CVarDef<float> ThermalDetectionMultiplier =
        CVarDef.Create("mono.detection.thermal_multiplier", 2f, CVar.ARCHIVE | CVar.REPLICATED);

    /// <summary>
    ///     Multiplier of grid visual detection radius.
    /// </summary>
    public static readonly CVarDef<float> VisualDetectionMultiplier =
        CVarDef.Create("mono.detection.visual_multiplier", 16f, CVar.ARCHIVE | CVar.REPLICATED);

    #endregion
}
