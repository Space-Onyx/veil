/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server.GameTicking;
using Content.Server._Onyx.ZLevels.Core.Components;
using Content.Shared._Onyx.ZLevels.Core.Components;
using Content.Shared._Onyx.ZLevels.Core.EntitySystems;
using Content.Shared.DeviceLinking;
using Robust.Server.GameObjects;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.Station.Events;
using Content.Server._Utopia.ZLevels.Transmission.Systems;
using Content.Server._Onyx.ZLevels.Atmos;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Events;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.ZLevels.Core;

public sealed partial class CEZLevelsSystem : CESharedZLevelsSystem
{
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly CEZLevelsSystem _zLevels = default!;
    [Dependency] private readonly ZLevelTransmissionSystem _zTransmission = default!;
    [Dependency] private readonly ZLevelGridAtmosSystem _zLevelGridAtmos = default!;
    [Dependency] private readonly SharedDeviceLinkSystem _deviceLink = default!;

    public override void Initialize()
    {
        base.Initialize();
        InitView();

        SubscribeLocalEvent<PostGameMapLoad>(OnGameMapLoad);
        SubscribeLocalEvent<CEStationZLevelsComponent, StationPostInitEvent>(OnStationPostInit);
        SubscribeLocalEvent<BeforeSerializationEvent>(OnBeforeSerialization);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateView(frameTime);
    }

    private void OnStationPostInit(Entity<CEStationZLevelsComponent> ent, ref StationPostInitEvent args)
    {
        if (ent.Comp.MapsAbove.Count == 0 && ent.Comp.MapsBelow.Count == 0)
            return;

        var stationName = MetaData(ent).EntityName;
        var stationNetwork = _zLevels.CreateZNetwork();
        ent.Comp.ZNetworkEntity = stationNetwork;
        _meta.SetEntityName(ent.Comp.ZNetworkEntity.Value, $"Station z-Network: {stationName}");

        var mainGrid = _station.GetLargestGrid(ent.Owner);

        if (mainGrid is null)
        {
            Log.Error($"Station {ent.Owner} has no grids to base z-levels off of. Skipping Z-network initialization.");
            ent.Comp.ZNetworkEntity = null;
            QueueDel(stationNetwork);
            return;
        }

        var mainMap = Transform(mainGrid.Value).MapUid;
        if (mainMap is null)
        {
            Log.Error($"Station {ent.Owner} main grid {mainGrid.Value} has no map entity. Skipping Z-network initialization.");
            ent.Comp.ZNetworkEntity = null;
            QueueDel(stationNetwork);
            return;
        }

        string? stationId = null;
        if (TryComp<BecomesStationComponent>(mainGrid.Value, out var mainBecomes))
            stationId = mainBecomes.Id;

        Dictionary<EntityUid, int> dict = new();
        dict.Add(mainMap.Value, 0);

        var mapsToInit = new List<MapId>();

        //Loading maps below first
        var depth = ent.Comp.MapsBelow.Count * -1;
        foreach (var mapBelow in ent.Comp.MapsBelow)
        {
            if (!_mapLoader.TryLoadMap(mapBelow, out var mapEnt, out var grids))
            {
                Log.Error($"Failed to load map for Station zNetwork at depth {depth}!");
                continue;
            }

            Log.Info($"Created map {mapEnt.Value.Comp.MapId} for Station zNetwork at level {depth}");
            _meta.SetEntityName(mapEnt.Value, $"{stationName} [{depth}]");

            if (stationId != null)
            {
                foreach (var grid in grids)
                {
                    if (TryComp<BecomesStationComponent>(grid, out var becomes) && becomes.Id == stationId)
                        _station.AddGridToStation(ent, grid);
                }
            }

            dict.Add(mapEnt.Value, depth);
            mapsToInit.Add(mapEnt.Value.Comp.MapId);
            depth++;
        }

        //Loading maps above next
        depth = 1;
        foreach (var mapAbove in ent.Comp.MapsAbove)
        {
            if (!_mapLoader.TryLoadMap(mapAbove, out var mapEnt, out var grids))
            {
                Log.Error($"Failed to load map for Station zNetwork at depth {depth}!");
                continue;
            }

            Log.Info($"Created map {mapEnt.Value.Comp.MapId} for Station zNetwork at level {depth}");
            _meta.SetEntityName(mapEnt.Value, $"{stationName} [{depth}]");

            if (stationId != null)
            {
                foreach (var grid in grids)
                {
                    if (TryComp<BecomesStationComponent>(grid, out var becomes) && becomes.Id == stationId)
                        _station.AddGridToStation(ent, grid);
                }
            }

            dict.Add(mapEnt.Value, depth);
            mapsToInit.Add(mapEnt.Value.Comp.MapId);
            depth++;
        }

        TryAddMapsIntoZNetwork(stationNetwork, dict);

        foreach (var mapId in mapsToInit)
        {
            _map.InitializeMap(mapId);
        }

        var mapSet = new HashSet<EntityUid>(dict.Keys);
        StabilizeZPhysicsAfterMapInit(mapSet);
        _zTransmission.RefreshTransmittersOnMaps(mapSet);

        RestoreCrossMapDeviceLinks(dict);
    }

    private void OnGameMapLoad(PostGameMapLoad ev)
    {
        if (ev.GameMap.MapsAbove.Count == 0 && ev.GameMap.MapsBelow.Count == 0)
            return;

        var stationNetwork = CreateZNetwork();
        _meta.SetEntityName(stationNetwork, $"Station z-Network: {ev.GameMap.MapName}");

        var mainMap = _map.GetMap(ev.Map);
        Dictionary<EntityUid, int> dict = new();
        dict.Add(mainMap, 0);

        EntityManager.AddComponents(mainMap, ev.GameMap.ZLevelsComponentOverrides);

        var mapsToInit = new List<MapId>();

        //Loading maps below first
        var depth = ev.GameMap.MapsBelow.Count * -1;
        foreach (var mapBelow in ev.GameMap.MapsBelow)
        {
            if (!_mapLoader.TryLoadMap(mapBelow, out var mapEnt, out _))
            {
                Log.Error($"Failed to load map for Station zNetwork at depth {depth}!");
                continue;
            }

            Log.Info($"Created map {mapEnt.Value.Comp.MapId} for Station zNetwork at level {depth}");
            EntityManager.AddComponents(mapEnt.Value, ev.GameMap.ZLevelsComponentOverrides);
            _meta.SetEntityName(mapEnt.Value, $"{ev.GameMap.MapName} [{depth}]");
            dict.Add(mapEnt.Value, depth);
            mapsToInit.Add(mapEnt.Value.Comp.MapId);
            depth++;
        }

        //Loading maps above next
        depth = 1;
        foreach (var mapAbove in ev.GameMap.MapsAbove)
        {
            if (!_mapLoader.TryLoadMap(mapAbove, out var mapEnt, out _))
            {
                Log.Error($"Failed to load map for Station zNetwork at depth {depth}!");
                continue;
            }

            Log.Info($"Created map {mapEnt.Value.Comp.MapId} for Station zNetwork at level {depth}");
            EntityManager.AddComponents(mapEnt.Value, ev.GameMap.ZLevelsComponentOverrides);
            _meta.SetEntityName(mapEnt.Value, $"{ev.GameMap.MapName} [{depth}]");
            dict.Add(mapEnt.Value, depth);
            mapsToInit.Add(mapEnt.Value.Comp.MapId);
            depth++;
        }

        TryAddMapsIntoZNetwork(stationNetwork, dict);

        foreach (var mapId in mapsToInit)
        {
            _map.InitializeMap(mapId);
        }

        var mapSet = new HashSet<EntityUid>(dict.Keys);
        StabilizeZPhysicsAfterMapInit(mapSet);
        _zTransmission.RefreshTransmittersOnMaps(mapSet);

        RestoreCrossMapDeviceLinks(dict);
    }

    #region Cross-Map Device Links // <Onyx-Tweak>

    /// <summary>
    /// Before map serialization, collect cross-map device links and store them
    /// on map entities so they persist in map yml files.
    /// </summary>
    private void OnBeforeSerialization(BeforeSerializationEvent ev)
    {
        // Find all Z-networks that overlap with the maps being saved
        var networkQuery = EntityQueryEnumerator<CEZLevelsNetworkComponent>();
        while (networkQuery.MoveNext(out _, out var network))
        {
            // Build mapUid → depth for this network
            var depthByMap = new Dictionary<EntityUid, int>();
            foreach (var (depth, mapUid) in network.ZLevels)
            {
                if (mapUid is not { } uid)
                    continue;
                depthByMap[uid] = depth;
            }

            if (depthByMap.Count < 2)
                continue;

            // Collect cross-map links
            var links = CollectCrossMapLinks(depthByMap);
            if (links.Count == 0)
                continue;

            // Store on each map entity in the network
            foreach (var (mapUid, _) in depthByMap)
            {
                if (!ev.Entities.Contains(mapUid))
                    continue;

                var comp = EnsureComp<CEZCrossMapLinksComponent>(mapUid);
                comp.DeviceLinks = links;
                Dirty(mapUid, comp);
            }
        }
    }

    private List<CEZCrossMapDeviceLink> CollectCrossMapLinks(Dictionary<EntityUid, int> depthByMap)
    {
        var result = new List<CEZCrossMapDeviceLink>();

        var sourceQuery = EntityManager.AllEntityQueryEnumerator<DeviceLinkSourceComponent, TransformComponent, MetaDataComponent>();
        while (sourceQuery.MoveNext(out var sourceUid, out var source, out var sourceXform, out var sourceMeta))
        {
            var sourceMapUid = sourceXform.MapUid;
            if (sourceMapUid == null || !depthByMap.TryGetValue(sourceMapUid.Value, out var sourceDepth))
                continue;

            foreach (var (sinkUid, links) in source.LinkedPorts)
            {
                if (!EntityManager.TryGetComponent<TransformComponent>(sinkUid, out var sinkXform))
                    continue;

                var sinkMapUid = sinkXform.MapUid;
                if (sinkMapUid == null || !depthByMap.TryGetValue(sinkMapUid.Value, out var sinkDepth))
                    continue;

                if (sourceMapUid == sinkMapUid)
                    continue;

                var sourceProto = sourceMeta.EntityPrototype?.ID ?? "";
                var sinkMeta = EntityManager.GetComponent<MetaDataComponent>(sinkUid);
                var sinkProto = sinkMeta.EntityPrototype?.ID ?? "";

                var sourcePos = sourceXform.LocalPosition;
                var sinkPos = sinkXform.LocalPosition;

                var link = new CEZCrossMapDeviceLink
                {
                    SourceDepth = sourceDepth,
                    SourceX = MathF.Round(sourcePos.X, 2),
                    SourceY = MathF.Round(sourcePos.Y, 2),
                    SourcePrototype = sourceProto,
                    SinkDepth = sinkDepth,
                    SinkX = MathF.Round(sinkPos.X, 2),
                    SinkY = MathF.Round(sinkPos.Y, 2),
                    SinkPrototype = sinkProto,
                };

                foreach (var (srcPort, snkPort) in links)
                {
                    link.PortLinks.Add($"{srcPort.Id}:{snkPort.Id}");
                }

                result.Add(link);
            }
        }

        return result;
    }

    /// <summary>
    /// Restore cross-map device links after all Z-level maps have been loaded.
    /// Reads from CEZCrossMapLinksComponent on any of the loaded map entities.
    /// </summary>
    private void RestoreCrossMapDeviceLinks(Dictionary<EntityUid, int> maps)
    {
        // Find saved links from any map entity
        List<CEZCrossMapDeviceLink>? savedLinks = null;
        foreach (var (mapUid, _) in maps)
        {
            if (!TryComp<CEZCrossMapLinksComponent>(mapUid, out var linksComp))
                continue;
            if (linksComp.DeviceLinks.Count == 0)
                continue;
            savedLinks = linksComp.DeviceLinks;
            break;
        }

        if (savedLinks == null || savedLinks.Count == 0)
            return;

        // Build depth → mapUid
        var mapByDepth = new Dictionary<int, EntityUid>();
        foreach (var (mapUid, depth) in maps)
            mapByDepth[depth] = mapUid;

        // Build entity lookup: (depth, x, y, proto) → EntityUid
        var entityLookup = new Dictionary<(int, float, float, string), EntityUid>();

        var sourceEnum = EntityManager.AllEntityQueryEnumerator<DeviceLinkSourceComponent, TransformComponent, MetaDataComponent>();
        while (sourceEnum.MoveNext(out var uid, out _, out var xform, out var meta))
        {
            if (xform.MapUid == null)
                continue;
            foreach (var (mu, d) in maps)
            {
                if (mu != xform.MapUid.Value)
                    continue;
                var key = (d, MathF.Round(xform.LocalPosition.X, 2), MathF.Round(xform.LocalPosition.Y, 2), meta.EntityPrototype?.ID ?? "");
                entityLookup.TryAdd(key, uid);
                break;
            }
        }

        var sinkEnum = EntityManager.AllEntityQueryEnumerator<DeviceLinkSinkComponent, TransformComponent, MetaDataComponent>();
        while (sinkEnum.MoveNext(out var uid, out _, out var xform, out var meta))
        {
            if (xform.MapUid == null)
                continue;
            foreach (var (mu, d) in maps)
            {
                if (mu != xform.MapUid.Value)
                    continue;
                var key = (d, MathF.Round(xform.LocalPosition.X, 2), MathF.Round(xform.LocalPosition.Y, 2), meta.EntityPrototype?.ID ?? "");
                entityLookup.TryAdd(key, uid);
                break;
            }
        }

        // Restore links
        foreach (var link in savedLinks)
        {
            var sourceKey = (link.SourceDepth, link.SourceX, link.SourceY, link.SourcePrototype);
            var sinkKey = (link.SinkDepth, link.SinkX, link.SinkY, link.SinkPrototype);

            if (!entityLookup.TryGetValue(sourceKey, out var sourceUid))
                continue;
            if (!entityLookup.TryGetValue(sinkKey, out var sinkUid))
                continue;

            if (!EntityManager.HasComponent<DeviceLinkSourceComponent>(sourceUid))
                continue;
            if (!EntityManager.HasComponent<DeviceLinkSinkComponent>(sinkUid))
                continue;

            var portLinks = new List<(string source, string sink)>();
            foreach (var portStr in link.PortLinks)
            {
                var parts = portStr.Split(':');
                if (parts.Length == 2)
                    portLinks.Add((parts[0], parts[1]));
            }

            if (portLinks.Count > 0)
                _deviceLink.RestoreLinks(sourceUid, sinkUid, portLinks);
        }
    }

    #endregion // </Onyx-Tweak>
}
