using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent]
public sealed partial class AugmentPassivePowerDrawComponent : Component
{
    [DataField(required: true)]
    public float Draw;

    [DataField]
    public string TooltipSource = "neuro-interface-tooltip-source-power-passive-generic";

    [DataField]
    public bool Enabled = true;
}