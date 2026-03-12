using Content.Shared.Nutrition.Components;
using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AugmentReactorComponent : Component
{
    [DataField, AutoNetworkedField]
    public float ChargeRate = 1f;

    [DataField, AutoNetworkedField]
    public float HungerCostPerCharge = 1f;

    [DataField, AutoNetworkedField]
    public HungerThreshold MinimumHungerThreshold = HungerThreshold.Starving;
}
