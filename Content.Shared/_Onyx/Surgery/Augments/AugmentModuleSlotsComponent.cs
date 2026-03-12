using System.Collections.Generic;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AugmentModuleSlotsComponent : Component
{
    [DataField(required: true)]
    public List<AugmentModuleSlotDefinition> Slots = new();

    [DataField, AutoNetworkedField]
    public bool PanelOpen;
}

[DataDefinition]
public sealed partial class AugmentModuleSlotDefinition
{
    [DataField(required: true)]
    public string Id = string.Empty;

    [DataField]
    public string Name = "augment-modules-slot-default-name";

    [DataField]
    public bool AllowInsertWhenUninstalled = true;

    [DataField]
    public bool AllowInsertWhenInstalled = true;

    [DataField]
    public bool VisibleInVerbs = true;

    [DataField]
    public EntityWhitelist? Whitelist;

}

[ByRefEvent]
public record struct AugmentModulePanelStateChangedEvent(EntityUid? Body, bool Open);

[ByRefEvent]
public record struct AugmentModuleInsertAttemptEvent(
    EntityUid Augment,
    string SlotId,
    EntityUid Module,
    EntityUid? User,
    EntityUid? Body,
    bool Cancelled = false);

[ByRefEvent]
public record struct AugmentModuleEjectAttemptEvent(
    EntityUid Augment,
    string SlotId,
    EntityUid Module,
    EntityUid? User,
    EntityUid? Body,
    bool Cancelled = false);

[ByRefEvent]
public record struct AugmentModuleInsertedEvent(
    EntityUid Augment,
    string SlotId,
    EntityUid Module,
    EntityUid? Body);

[ByRefEvent]
public record struct AugmentModuleRemovedEvent(
    EntityUid Augment,
    string SlotId,
    EntityUid Module,
    EntityUid? Body);
