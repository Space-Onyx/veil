using Content.Shared._Onyx.Swimming.Components;
using Content.Shared.Movement.Events;

namespace Content.Shared._Onyx.Swimming.Systems;

public sealed class SharedOceanSwimmingSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OceanSwimmingComponent, CanWeightlessMoveEvent>(OnCanWeightlessMove);
    }

    private void OnCanWeightlessMove(Entity<OceanSwimmingComponent> ent, ref CanWeightlessMoveEvent args)
    {
        args.CanMove = true;
    }
}