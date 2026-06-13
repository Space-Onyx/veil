using System.Linq;
using Content.Shared._Onyx.Food.Components;
using Content.Shared._Onyx.Food.Systems;
using Robust.Shared.Containers;

namespace Content.Server._Onyx.Food;

public sealed class PlateContainerEjectionSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly PlateContainerSystem _plate = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlateContainerComponent, EntityTerminatingEvent>(OnTerminating);
    }

    private void OnTerminating(Entity<PlateContainerComponent> ent, ref EntityTerminatingEvent args)
    {
        var plateTransform = Transform(ent.Owner);
        if (TryComp(plateTransform.ParentUid, out MetaDataComponent? parentMeta) &&
            parentMeta.EntityLifeStage >= EntityLifeStage.Terminating)
        {
            return;
        }

        var container = _plate.GetContainer(ent.Owner);
        foreach (var item in container.ContainedEntities.ToArray())
        {
            _container.Remove(
                item,
                container,
                force: true,
                destination: plateTransform.Coordinates);
        }
    }
}
