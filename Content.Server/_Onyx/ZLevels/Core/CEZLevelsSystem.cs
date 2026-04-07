/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server.GameTicking;
using Content.Server._Onyx.ZLevels.Core.Components;
using Content.Shared._Onyx.ZLevels.Core.EntitySystems;
using Robust.Server.GameObjects;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.Station.Events;
using Content.Server._Utopia.ZLevels.Transmission.Systems;
using Content.Server._Onyx.ZLevels.Atmos;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
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

    public override void Initialize()
    {
        base.Initialize();
        InitView();

        SubscribeLocalEvent<PostGameMapLoad>(OnGameMapLoad);
        SubscribeLocalEvent<CEStationZLevelsComponent, StationPostInitEvent>(OnStationPostInit);
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
            _map.InitializeMap(mapEnt.Value.Comp.MapId);
            _meta.SetEntityName(mapEnt.Value, $"{ev.GameMap.MapName} [{depth}]");
            dict.Add(mapEnt.Value, depth);
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
            _map.InitializeMap(mapEnt.Value.Comp.MapId);
            _meta.SetEntityName(mapEnt.Value, $"{ev.GameMap.MapName} [{depth}]");
            dict.Add(mapEnt.Value, depth);
            depth++;
        }

        TryAddMapsIntoZNetwork(stationNetwork, dict);
        var mapSet = new HashSet<EntityUid>(dict.Keys);
        StabilizeZPhysicsAfterMapInit(mapSet);
        _zTransmission.RefreshTransmittersOnMaps(mapSet);
    }
}
