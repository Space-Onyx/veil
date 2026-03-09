namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent]
public sealed partial class AugmentUniversalModuleAccumulatorComponent : Component
{
    [DataField]
    public float MaxNeuroLoadDelta;

    [DataField]
    public float CurrentNeuroLoadDelta;

    [DataField]
    public float PassivePowerDrawDelta;

    [DataField]
    public float VisionActivePowerMultiplier = 1f;

    [DataField]
    public float VisionActivePowerDelta;

    [DataField]
    public float VisionActiveNeuroMultiplier = 1f;

    [DataField]
    public float VisionActiveNeuroDelta;

    [DataField]
    public float ItemPanelActivePowerMultiplier = 1f;

    [DataField]
    public float ItemPanelActivePowerDelta;

    [DataField]
    public float ItemPanelActiveNeuroMultiplier = 1f;

    [DataField]
    public float ItemPanelActiveNeuroDelta;

    [DataField]
    public string NeuroLoadTooltipSource = "neuro-interface-tooltip-source-neuro-module-passive";

    [DataField]
    public string PowerTooltipSource = "neuro-interface-tooltip-source-power-module-passive";
}
