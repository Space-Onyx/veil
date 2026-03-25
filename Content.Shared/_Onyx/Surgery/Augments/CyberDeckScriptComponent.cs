using Content.Shared.Actions;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CyberDeckScriptComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Action = default!;

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    [DataField]
    public float RamCost = 4f;

    [DataField]
    public int AciPenetrationLevel = 1;
}
public sealed partial class CyberDeckScriptActionEvent : InstantActionEvent;
public sealed partial class CyberDeckScriptTargetActionEvent : WorldTargetActionEvent;

[ByRefEvent]
public record struct CyberDeckScriptExecutedEvent(
    EntityUid Body,
    EntityUid CyberDeck,
    EntityUid Performer,
    EntityUid? TargetEntity = null,
    EntityCoordinates? TargetCoordinates = null,
    float AciTimeMultiplier = 1f);
