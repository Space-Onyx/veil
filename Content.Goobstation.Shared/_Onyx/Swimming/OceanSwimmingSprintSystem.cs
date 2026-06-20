using Content.Goobstation.Shared.Sprinting;
using Content.Shared._Onyx.Swimming.Events;

namespace Content.Goobstation.Shared._Onyx.Swimming;

public sealed class OceanSwimmingSprintSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SprinterComponent, OceanSwimmingSprintEvent>(OnGetSprint);
    }

    private void OnGetSprint(Entity<SprinterComponent> ent, ref OceanSwimmingSprintEvent args)
    {
        args.IsSprinting = ent.Comp.IsSprinting;
        args.StaminaDrainKey = ent.Comp.StaminaDrainKey;
        args.StaminaRegenMultiplier = ent.Comp.StaminaRegenMultiplier;
    }
}
