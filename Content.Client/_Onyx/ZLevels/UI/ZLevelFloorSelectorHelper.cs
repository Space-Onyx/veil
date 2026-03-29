using System.Collections.Generic;
using System.Linq;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Pinpointer;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._Onyx.ZLevels.UI;

public readonly record struct ZLevelFloorSelectorState(
    bool HasFloorSelection,
    List<int> Floors,
    int SelectedFloor,
    int MonitorFloor,
    EntityUid? SelectedMap);

public static class ZLevelFloorSelectorHelper
{
    public static ZLevelFloorSelectorState GetFloorState(IEntityManager entMan, EntityUid owner, int? selectedFloorDepth)
    {
        var monitorMap = ResolveEntityMapUid(entMan, owner);
        if (monitorMap == null || !entMan.TryGetComponent<CEZLevelMapComponent>(monitorMap.Value, out var monitorZMap))
            return new ZLevelFloorSelectorState(false, new List<int> { 0 }, 0, 0, monitorMap);

        var monitorDepth = monitorZMap.Depth;
        var mapByDepth = new Dictionary<int, EntityUid>
        {
            [monitorDepth] = monitorMap.Value
        };

        if (TryGetNetwork(entMan, monitorMap.Value, out var network))
        {
            foreach (var mapUid in network.ZLevels.Values)
            {
                if (!mapUid.HasValue)
                    continue;

                if (!entMan.TryGetComponent<CEZLevelMapComponent>(mapUid.Value, out var zMap))
                    continue;

                mapByDepth[zMap.Depth] = mapUid.Value;
            }
        }

        var floors = mapByDepth.Keys.OrderBy(x => x).ToList();
        if (floors.Count <= 1)
            return new ZLevelFloorSelectorState(false, new List<int> { monitorDepth }, monitorDepth, monitorDepth, monitorMap);

        var selected = selectedFloorDepth.HasValue && floors.Contains(selectedFloorDepth.Value)
            ? selectedFloorDepth.Value
            : monitorDepth;

        return new ZLevelFloorSelectorState(true, floors, selected, monitorDepth, mapByDepth.GetValueOrDefault(selected, monitorMap.Value));
    }

    public static string FormatRelativeFloor(int floor, int monitorFloor)
    {
        var relative = floor - monitorFloor;
        return relative > 0 ? $"+{relative}" : relative.ToString();
    }

    public static EntityUid? ResolveNavMapUidForMap(IEntityManager entMan, EntityUid? mapUid)
    {
        if (mapUid == null)
            return null;

        if (entMan.HasComponent<MapGridComponent>(mapUid.Value))
            return mapUid.Value;

        if (entMan.HasComponent<NavMapComponent>(mapUid.Value))
            return mapUid.Value;

        EntityUid? fallbackGrid = null;
        var grids = entMan.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (grids.MoveNext(out var gridUid, out _, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (entMan.HasComponent<NavMapComponent>(gridUid))
                return gridUid;

            fallbackGrid ??= gridUid;
        }

        return fallbackGrid;
    }

    public static EntityUid? ResolveNavMapUidForOwner(IEntityManager entMan, EntityUid owner, EntityUid? selectedMap)
    {
        var resolvedForMap = ResolveNavMapUidForMap(entMan, selectedMap);
        if (resolvedForMap != null)
            return resolvedForMap;

        if (!entMan.TryGetComponent<TransformComponent>(owner, out var ownerXform))
            return null;

        if (ownerXform.GridUid != null)
            return ownerXform.GridUid.Value;

        if (ownerXform.MapUid != null)
            return ResolveNavMapUidForMap(entMan, ownerXform.MapUid.Value);

        return null;
    }

    public static bool TryGetEntityDepth(IEntityManager entMan, EntityUid entity, out int depth)
    {
        depth = 0;

        var mapUid = ResolveEntityMapUid(entMan, entity);
        if (mapUid == null || !entMan.TryGetComponent<CEZLevelMapComponent>(mapUid.Value, out var zMap))
            return false;

        depth = zMap.Depth;
        return true;
    }

    public static bool TryGetEntityMapUid(IEntityManager entMan, EntityUid entity, out EntityUid mapUid)
    {
        var resolved = ResolveEntityMapUid(entMan, entity);
        if (resolved == null)
        {
            mapUid = default;
            return false;
        }

        mapUid = resolved.Value;
        return true;
    }

    public static bool TryGetCoordinatesMapUid(IEntityManager entMan, NetCoordinates coords, out EntityUid mapUid)
    {
        var entityCoordinates = entMan.GetCoordinates(coords);
        return TryGetEntityMapUid(entMan, entityCoordinates.EntityId, out mapUid);
    }

    private static EntityUid? ResolveEntityMapUid(IEntityManager entMan, EntityUid entity)
    {
        if (entMan.TryGetComponent<TransformComponent>(entity, out var xform))
        {
            if (xform.MapUid != null)
                return xform.MapUid.Value;

            if (xform.GridUid != null &&
                entMan.TryGetComponent<TransformComponent>(xform.GridUid.Value, out var gridXform) &&
                gridXform.MapUid != null)
            {
                return gridXform.MapUid.Value;
            }
        }

        return null;
    }

    private static bool TryGetNetwork(IEntityManager entMan, EntityUid mapUid, out CEZLevelsNetworkComponent network)
    {
        var query = entMan.EntityQueryEnumerator<CEZLevelsNetworkComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            foreach (var map in comp.ZLevels.Values)
            {
                if (map != mapUid)
                    continue;

                network = comp;
                return true;
            }
        }

        network = default!;
        return false;
    }
}
