using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Onyx.Surgery.Augments;

[RegisterComponent, NetworkedComponent]
public sealed partial class AugmentHoloPdaComponent : Component
{
    public const string HoloPdaIdSlotId = "HoloPDA-id";
    public const string HoloPdaBodyIdSlotId = "HoloPDA-body-id";
    public const string HoloPdaCartridgeSlotId = "HoloPDA-cartridge";

    [DataField]
    public EntityUid? ActionEntity;

    [DataField]
    public SoundSpecifier EjectSound = new SoundPathSpecifier("/Audio/Machines/id_swipe.ogg");

    [DataField]
    public ItemSlot IdSlot = new();

    [DataField]
    public ItemSlot BodyIdSlot = new();

    [DataField]
    public ItemSlot CartridgeSlot = new();
}
