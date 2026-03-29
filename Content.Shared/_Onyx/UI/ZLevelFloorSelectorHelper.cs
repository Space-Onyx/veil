using Content.Shared.Pinpointer;
using Content.Shared.Station.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using System.Linq;

namespace Content.Shared._Onyx.UI;

public readonly struct ZLevelFloorSelectorState
{
    public bool HasFloorSelection { get; }
    public List<int> Floors { get; }
    public int SelectedFloor { get; }
    public EntityUid? SelectedMap { get; }
    public int SourceFloor { get; }

    public ZLevelFloorSelectorState(
        bool hasFloorSelection,
        List<int> floors,
        int selectedFloor,
        EntityUid? selectedMap,
        int sourceFloor)
    {
        HasFloorSelection = hasFloorSelection;
        Floors = floors;
        SelectedFloor = selectedFloor;
        SelectedMap = selectedMap;
        SourceFloor = sourceFloor;
    }
}

public static class ZLevelFloorSelectorHelper
{
    public static ZLevelFloorSelectorState GetFloorState(
        IEntityManager entityManager,
        CESharedZLevelsSystem zLevels,
        EntityUid sourceUid,
        int? selectedFloor)
    {
        if (!entityManager.TryGetComponent<TransformComponent>(sourceUid, out var sourceXform))
            return new ZLevelFloorSelectorState(false, new List<int> { 0 }, 0, null, 0);

        var sourceMap = sourceXform.MapUid;
        var sourceGrid = sourceXform.GridUid;
        if (sourceMap == null ||
            !entityManager.TryGetComponent<CEZLevelMapComponent>(sourceMap.Value, out var sourceZMap))
        {
            var fallbackMap = sourceGrid ?? sourceMap;
            return new ZLevelFloorSelectorState(false, new List<int> { 0 }, 0, fallbackMap, 0);
        }

        var sourceDepth = sourceZMap.Depth;
        var sourceStation = EntityUid.Invalid;
        if (sourceGrid != null &&
            entityManager.TryGetComponent<StationMemberComponent>(sourceGrid.Value, out var stationMember))
        {
            sourceStation = stationMember.Station;
        }

        var sourceFloorTarget = sourceGrid ?? sourceMap.Value;

        var mapByDepth = new Dictionary<int, EntityUid>
        {
            [sourceDepth] = sourceFloorTarget
        };

        foreach (var mapUid in zLevels.GetAllMapsBelow((sourceMap.Value, sourceZMap)))
        {
            if (!entityManager.TryGetComponent<CEZLevelMapComponent>(mapUid, out var zMap))
                continue;

            mapByDepth[zMap.Depth] = ResolveGridForMap(entityManager, mapUid, sourceStation) ?? mapUid;
        }

        foreach (var mapUid in zLevels.GetAllMapsAbove((sourceMap.Value, sourceZMap)))
        {
            if (!entityManager.TryGetComponent<CEZLevelMapComponent>(mapUid, out var zMap))
                continue;

            mapByDepth[zMap.Depth] = ResolveGridForMap(entityManager, mapUid, sourceStation) ?? mapUid;
        }

        var floors = mapByDepth.Keys.OrderBy(x => x).ToList();
        if (floors.Count <= 1)
        {
            return new ZLevelFloorSelectorState(
                false,
                new List<int> { sourceDepth },
                sourceDepth,
                sourceFloorTarget,
                sourceDepth);
        }

        var normalizedSelected = selectedFloor;
        if (normalizedSelected == null || !floors.Contains(normalizedSelected.Value))
            normalizedSelected = sourceDepth;

        EntityUid? selectedMap = sourceMap;
        if (mapByDepth.TryGetValue(normalizedSelected.Value, out var levelMap))
            selectedMap = levelMap;

        return new ZLevelFloorSelectorState(true, floors, normalizedSelected.Value, selectedMap, sourceDepth);
    }

    public static Dictionary<TKey, TValue> FilterByDepth<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> source,
        IReadOnlyDictionary<TKey, int> depthLookup,
        int selectedFloor) where TKey : notnull
    {
        var filtered = new Dictionary<TKey, TValue>();

        foreach (var (key, value) in source)
        {
            if (!depthLookup.TryGetValue(key, out var depth) || depth != selectedFloor)
                continue;

            filtered[key] = value;
        }

        return filtered;
    }

    public static bool TryGetDepthForKey<TKey>(
        IReadOnlyDictionary<TKey, int> primaryDepthLookup,
        IReadOnlyDictionary<TKey, int> secondaryDepthLookup,
        TKey key,
        out int depth) where TKey : notnull
    {
        if (primaryDepthLookup.TryGetValue(key, out depth))
            return true;

        return secondaryDepthLookup.TryGetValue(key, out depth);
    }

    public static int GetSelectedFloorIndex(IReadOnlyList<int> floors, int selectedFloor)
    {
        for (var i = 0; i < floors.Count; i++)
        {
            if (floors[i] == selectedFloor)
                return i;
        }

        return 0;
    }

    public static string FormatRelativeFloor(int floor, int sourceFloor)
    {
        var relativeFloor = floor - sourceFloor;
        if (relativeFloor > 0)
            return $"+{relativeFloor}";

        return relativeFloor.ToString();
    }

    public static EntityUid? ResolveSelectedMapUid(IEntityManager entityManager, NetEntity? selectedMap)
    {
        if (selectedMap == null ||
            !entityManager.TryGetEntity(selectedMap.Value, out var selectedMapUid) ||
            selectedMapUid == null)
        {
            return null;
        }

        return ResolveMapUidFromEntity(entityManager, selectedMapUid.Value);
    }

    public static EntityUid? ResolveMapUid(IEntityManager entityManager, EntityUid? selectedMap)
    {
        if (selectedMap == null)
            return null;

        return ResolveMapUidFromEntity(entityManager, selectedMap.Value);
    }

    public static EntityUid? ResolveMapUidFromEntityOrCoordinates(
        IEntityManager entityManager,
        NetEntity netEntity,
        NetCoordinates coordinates)
    {
        if (entityManager.TryGetEntity(netEntity, out var uid) &&
            uid != null &&
            ResolveMapUidFromEntity(entityManager, uid.Value) is { } mapUid)
        {
            return mapUid;
        }

        return ResolveMapUid(entityManager, coordinates);
    }

    public static void EnsureNavMapsForLinkedFloors(
        IEntityManager entityManager,
        CESharedZLevelsSystem zLevels,
        EntityUid sourceUid)
    {
        if (!entityManager.TryGetComponent<TransformComponent>(sourceUid, out var xform))
            return;

        if (xform.MapUid == null)
        {
            if (xform.GridUid != null)
                entityManager.EnsureComponent<NavMapComponent>(xform.GridUid.Value);
            return;
        }

        if (!entityManager.TryGetComponent<CEZLevelMapComponent>(xform.MapUid.Value, out var sourceZMap))
        {
            if (xform.GridUid != null)
                entityManager.EnsureComponent<NavMapComponent>(xform.GridUid.Value);
            return;
        }

        var targetMaps = new HashSet<EntityUid> { xform.MapUid.Value };
        foreach (var mapUid in zLevels.GetAllMapsAbove((xform.MapUid.Value, sourceZMap)))
            targetMaps.Add(mapUid);
        foreach (var mapUid in zLevels.GetAllMapsBelow((xform.MapUid.Value, sourceZMap)))
            targetMaps.Add(mapUid);

        var gridQuery = entityManager.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (gridQuery.MoveNext(out var gridUid, out _, out var gridXform))
        {
            if (gridXform.MapUid == null || !targetMaps.Contains(gridXform.MapUid.Value))
                continue;

            entityManager.EnsureComponent<NavMapComponent>(gridUid);
        }
    }

    public static bool TryGetMapDepthForNetEntity(
        IEntityManager entityManager,
        NetEntity netEntity,
        out int depth)
    {
        depth = 0;

        if (!entityManager.TryGetEntity(netEntity, out var uid) || uid == null)
            return false;

        if (!entityManager.TryGetComponent<TransformComponent>(uid.Value, out var xform) ||
            xform.MapUid is not { } mapUid)
        {
            return false;
        }

        if (entityManager.TryGetComponent<CEZLevelMapComponent>(mapUid, out var zMap))
            depth = zMap.Depth;

        return true;
    }

    private static EntityUid? ResolveMapUid(IEntityManager entityManager, NetCoordinates coordinates)
    {
        var entityCoordinates = entityManager.GetCoordinates(coordinates);
        var source = entityCoordinates.EntityId;

        return ResolveMapUidFromEntity(entityManager, source);
    }

    private static EntityUid? ResolveMapUidFromEntity(IEntityManager entityManager, EntityUid uid)
    {
        if (entityManager.HasComponent<MapGridComponent>(uid))
            return uid;

        EntityUid? mapUid = null;

        if (entityManager.TryGetComponent<TransformComponent>(uid, out var xform))
        {
            if (xform.GridUid != null)
                return xform.GridUid;

            if (xform.MapUid != null)
                mapUid = xform.MapUid;
        }

        if (mapUid == null && entityManager.HasComponent<MapComponent>(uid))
            mapUid = uid;

        if (mapUid == null)
            return null;

        var gridQuery = entityManager.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (gridQuery.MoveNext(out var gridUid, out _, out var gridXform))
        {
            if (gridXform.MapUid == mapUid.Value)
                return gridUid;
        }

        return null;
    }

    private static EntityUid? ResolveGridForMap(
        IEntityManager entityManager,
        EntityUid mapUid,
        EntityUid sourceStation)
    {
        EntityUid? firstGrid = null;

        var gridQuery = entityManager.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (gridQuery.MoveNext(out var gridUid, out _, out var gridXform))
        {
            if (gridXform.MapUid != mapUid)
                continue;

            firstGrid ??= gridUid;

            if (sourceStation == EntityUid.Invalid)
                continue;

            if (entityManager.TryGetComponent<StationMemberComponent>(gridUid, out var stationMember) &&
                stationMember.Station == sourceStation)
            {
                return gridUid;
            }
        }

        return firstGrid;
    }
}
