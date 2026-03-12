using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent]
public sealed partial class AugmentUniversalModuleComponent : Component
{
    [DataField]
    public float MaxNeuroLoad;

    [DataField]
    public float CurrentNeuroLoad;

    [DataField]
    public float PassivePowerDraw;

    [DataField]
    public float VisionActivePowerMultiplier = AugmentUniversalModuleDefaults.NeutralMultiplier;

    [DataField]
    public float VisionActivePower;

    [DataField]
    public float VisionActiveNeuroMultiplier = AugmentUniversalModuleDefaults.NeutralMultiplier;

    [DataField]
    public float VisionActiveNeuro;

    [DataField]
    public float ItemPanelActivePowerMultiplier = AugmentUniversalModuleDefaults.NeutralMultiplier;

    [DataField]
    public float ItemPanelActivePower;

    [DataField]
    public float ItemPanelActiveNeuroMultiplier = AugmentUniversalModuleDefaults.NeutralMultiplier;

    [DataField]
    public float ItemPanelActiveNeuro;

    [DataField]
    public string NeuroLoadTooltipSource = AugmentUniversalModuleDefaults.PassiveNeuroTooltipSource;

    [DataField]
    public string PowerTooltipSource = AugmentUniversalModuleDefaults.PassivePowerTooltipSource;
}
