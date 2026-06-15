using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Surgery.Infections;

[RegisterComponent, NetworkedComponent]
public sealed partial class SurgeryInfectionProtectionComponent : Component
{
    [DataField]
    public float ChanceMultiplier = 1f;
}

[RegisterComponent]
public sealed partial class SurgicalSiteInfectionComponent : Component;

[ByRefEvent]
public record struct SurgeryInfectionAttemptEvent(EntityUid Surgeon, float Chance);
