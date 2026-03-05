using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Chat;
using Content.Shared.ADT.SpeechBarks;

namespace Content.Goobstation.Shared.Augments;

public sealed class AugmentRelaySystem : EntitySystem
{
    [Dependency] private readonly AugmentSystem _augment = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<InstalledAugmentsComponent, GetUserMeleeDamageEvent>(_augment.RelayEvent);
        // <Onyx-Surgery>
        SubscribeLocalEvent<InstalledAugmentsComponent, TransformSpeakerNameEvent>(_augment.RelayEvent);
        SubscribeLocalEvent<InstalledAugmentsComponent, TransformSpeakerBarkEvent>(_augment.RelayEvent);
        // </Onyx-Surgery>
    }
}
