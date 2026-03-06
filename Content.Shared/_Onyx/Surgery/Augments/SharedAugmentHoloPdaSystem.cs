using Content.Shared.Access.Components;
using Robust.Shared.Containers;

namespace Content.Shared._Onyx.Surgery.Augments;

public sealed class SharedAugmentHoloPdaSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AugmentHoloPdaComponent, GetAdditionalAccessEvent>(OnGetAdditionalAccess);
    }

    private void OnGetAdditionalAccess(Entity<AugmentHoloPdaComponent> ent, ref GetAdditionalAccessEvent args)
    {
        if (_containers.TryGetContainer(ent, AugmentHoloPdaComponent.HoloPdaIdSlotId, out var idContainer)
            && idContainer.ContainedEntities.Count > 0)
        {
            args.Entities.Add(idContainer.ContainedEntities[0]);
        }
    }
}
