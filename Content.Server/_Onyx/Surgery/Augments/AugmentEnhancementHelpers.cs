using Content.Goobstation.Shared.Augments;
using Content.Server.Body.Systems;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Containers.ItemSlots;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared._Shitmed.Cybernetics;
using Robust.Shared.GameObjects;

namespace Content.Server._Onyx.Surgery.Augments;

internal static class AugmentEnhancementHelpers
{
    public static IEnumerable<EntityUid> EnumeratePartEnhancements(
        EntityUid partUid,
        BodyPartComponent partComp,
        BodySystem bodySystem,
        ItemSlotsSystem itemSlots,
        IEntityManager entMan,
        bool includeModules = true)
    {
        var visited = new HashSet<EntityUid>();
        foreach (var enhancement in EnumeratePartEnhancementsInternal(
                     partUid,
                     partComp,
                     bodySystem,
                     itemSlots,
                     entMan,
                     visited,
                     includeModules))
        {
            yield return enhancement;
        }
    }

    public static IEnumerable<EntityUid> EnumerateEnhancements(
        EntityUid body,
        BodySystem bodySystem,
        ItemSlotsSystem itemSlots,
        IEntityManager entMan,
        bool includeModules = true)
    {
        var visited = new HashSet<EntityUid>();

        foreach (var (partUid, partComp) in bodySystem.GetBodyChildren(body))
        {
            foreach (var enhancement in EnumeratePartEnhancementsInternal(
                         partUid,
                         partComp,
                         bodySystem,
                         itemSlots,
                         entMan,
                         visited,
                         includeModules))
            {
                yield return enhancement;
            }
        }
    }

    private static IEnumerable<EntityUid> EnumeratePartEnhancementsInternal(
        EntityUid partUid,
        BodyPartComponent partComp,
        BodySystem bodySystem,
        ItemSlotsSystem itemSlots,
        IEntityManager entMan,
        HashSet<EntityUid> visited,
        bool includeModules)
    {
        if (entMan.TryGetComponent<CyberneticsComponent>(partUid, out _)
            && visited.Add(partUid))
        {
            yield return partUid;
        }

        foreach (var (organUid, _) in bodySystem.GetPartOrgans(partUid, partComp))
        {
            if (!entMan.TryGetComponent<AugmentComponent>(organUid, out _)
                && !entMan.TryGetComponent<CyberneticsComponent>(organUid, out _))
            {
                continue;
            }

            if (!visited.Add(organUid))
                continue;

            yield return organUid;

            if (!includeModules
                || !entMan.TryGetComponent<AugmentModuleSlotsComponent>(organUid, out var moduleSlots)
                || !entMan.TryGetComponent<ItemSlotsComponent>(organUid, out var itemSlotsComp))
            {
                continue;
            }

            foreach (var definition in moduleSlots.Slots)
            {
                if (!itemSlots.TryGetSlot(organUid, definition.Id, out var slot, itemSlotsComp))
                    continue;

                if (slot.Item is { } moduleUid && visited.Add(moduleUid))
                    yield return moduleUid;
            }
        }
    }

    public static EntityUid? TryGetOwningBody(EntityUid uid, IEntityManager entMan)
    {
        var current = uid;
        while (entMan.EntityExists(current))
        {
            if (entMan.TryGetComponent<OrganComponent>(current, out var organ) && organ.Body is { } body)
                return body;

            if (entMan.TryGetComponent<BodyPartComponent>(current, out var part) && part.Body is { } partBody)
                return partBody;

            if (!entMan.TryGetComponent<TransformComponent>(current, out var xform))
                break;

            var parent = xform.ParentUid;
            if (parent == current || parent == EntityUid.Invalid)
                break;

            current = parent;
        }

        return null;
    }
}
