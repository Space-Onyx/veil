using Robust.Shared.Containers;

namespace Content.Shared._Onyx.Surgery.Augments;

public abstract class SharedAugmentItemPanelSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentItemPanelActiveItemComponent, ContainerGettingRemovedAttemptEvent>(OnDropAttempt);
    }
    private void OnDropAttempt(Entity<AugmentItemPanelActiveItemComponent> ent, ref ContainerGettingRemovedAttemptEvent args)
    {
        args.Cancel();
    }
}
