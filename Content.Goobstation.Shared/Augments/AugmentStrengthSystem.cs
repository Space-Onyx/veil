using Content.Shared.Item.ItemToggle;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared.Body.Organ;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Goobstation.Shared.Augments;

public sealed class AugmentStrengthSystem : EntitySystem
{
    [Dependency] private readonly ItemToggleSystem _toggle = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentStrengthComponent, GetUserMeleeDamageEvent>(OnGetMeleeDamage);
        SubscribeLocalEvent<AugmentStrengthComponent, OrganEnableChangedEvent>(OnOrganEnableChanged); // <Onyx-Surgery>
    }

    private void OnGetMeleeDamage(Entity<AugmentStrengthComponent> ent, ref GetUserMeleeDamageEvent args)
    {
        // <Onyx-Surgery>
        if (TryComp<OrganComponent>(ent.Owner, out var organ) && !organ.Enabled)
            return;
        // </Onyx-Surgery>

        if (_toggle.IsActivated(ent.Owner))
            args.Damage *= ent.Comp.Modifier;
    }

    // <Onyx-Surgery>
    private void OnOrganEnableChanged(Entity<AugmentStrengthComponent> ent, ref OrganEnableChangedEvent args)
    {
        if (args.Enabled)
            return;

        _toggle.TryDeactivate(ent.Owner);
    }
    // </Onyx-Surgery>
}
