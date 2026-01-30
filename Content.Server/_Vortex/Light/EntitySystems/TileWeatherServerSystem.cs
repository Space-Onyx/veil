using Content.Server._Vortex.Weather.Components;
using Content.Shared._Vortex.Weather.Components;
using Content.Shared._Vortex.Weather.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Vortex.Wether.EntitySystems;

public sealed partial class TileWeatherServerSystem : SharedTileWeatherSystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    private EntityQuery<MapGridComponent> _gridQuery;

    // Track whether a marker has been applied (before deletion)
    private readonly HashSet<EntityUid> _appliedMarkers = new();

    public override void Initialize()
    {
        base.Initialize();

        _gridQuery = GetEntityQuery<MapGridComponent>();

        SubscribeLocalEvent<SetTileWeatherComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SetTileWeatherComponent, ComponentShutdown>(OnComponentShutdown);

        // Subscribe to grid removal to clean up weather data
        SubscribeLocalEvent<GridInitializeEvent>(OnGridInit);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoved);
    }

    private void OnGridInit(GridInitializeEvent ev)
    {
        if (HasComp<TileWeatherComponent>(ev.EntityUid))
            return;

        EnsureComp<TileWeatherComponent>(ev.EntityUid);
    }

    private void OnGridRemoved(GridRemovalEvent ev)
    {
        // Clean up weather data when grid is removed
        RemComp<TileWeatherComponent>(ev.EntityUid);
    }

    private void OnMapInit(Entity<SetTileWeatherComponent> ent, ref MapInitEvent args)
    {
        var xform = Transform(ent.Owner);
        var gridUid = xform.GridUid;

        // Always delete the marker after MapInit, regardless of placement
        var wasApplied = false;

        if (gridUid != null && _gridQuery.TryComp(gridUid, out var grid))
        {
            wasApplied = true;
            var tile = _map.GetTileRef((gridUid.Value, grid), xform.Coordinates);
            var tileIndices = tile.GridIndices;

            var chunkIndices = SharedMapSystem.GetChunkIndices(tileIndices, TileWeatherComponent.ChunkSize);
            var chunkRelative = SharedMapSystem.GetChunkRelative(tileIndices, TileWeatherComponent.ChunkSize);
            var bitFlag = (ulong) 1 << (chunkRelative.X + chunkRelative.Y * TileWeatherComponent.ChunkSize);

            var tileWeather = EnsureComp<TileWeatherComponent>(gridUid.Value);

            if (ent.Comp.Disable)
            {
                // Disable weather - clear enable flag and set disable flag
                if (tileWeather.EnableData.TryGetValue(chunkIndices, out var enableData))
                {
                    enableData &= ~bitFlag;
                    if (enableData == 0)
                        tileWeather.EnableData.Remove(chunkIndices);
                    else
                        tileWeather.EnableData[chunkIndices] = enableData;
                }

                if (!tileWeather.Data.TryGetValue(chunkIndices, out var chunkData))
                {
                    chunkData = 0;
                }

                chunkData |= bitFlag;
                tileWeather.Data[chunkIndices] = chunkData;
            }
            else
            {
                // Enable weather - clear disable flag and set enable flag
                if (tileWeather.Data.TryGetValue(chunkIndices, out var chunkData))
                {
                    chunkData &= ~bitFlag;
                    if (chunkData == 0)
                        tileWeather.Data.Remove(chunkIndices);
                    else
                        tileWeather.Data[chunkIndices] = chunkData;
                }

                if (!tileWeather.EnableData.TryGetValue(chunkIndices, out var enableData))
                {
                    enableData = 0;
                }

                enableData |= bitFlag;
                tileWeather.EnableData[chunkIndices] = enableData;
            }

            Dirty(gridUid.Value, tileWeather);
        }

        // Track as applied if we set a flag
        if (wasApplied)
            _appliedMarkers.Add(ent.Owner);

        // Delete the marker entity after it has done its job
        QueueDel(ent.Owner);
    }

    private void OnComponentShutdown(Entity<SetTileWeatherComponent> ent, ref ComponentShutdown args)
    {
        // Only clear the flag if this marker was actually applied (not deleted before MapInit)
        if (!_appliedMarkers.Remove(ent.Owner))
            return;

        if (_timing.ApplyingState)
            return;

        var xform = Transform(ent.Owner);
        if (xform.GridUid == null || !_gridQuery.TryComp(xform.GridUid, out var grid))
            return;

        var gridUid = xform.GridUid.Value;
        var tile = _map.GetTileRef((gridUid, grid), xform.Coordinates);
        var tileIndices = tile.GridIndices;

        var chunkIndices = SharedMapSystem.GetChunkIndices(tileIndices, TileWeatherComponent.ChunkSize);
        var chunkRelative = SharedMapSystem.GetChunkRelative(tileIndices, TileWeatherComponent.ChunkSize);
        var bitFlag = (ulong) 1 << (chunkRelative.X + chunkRelative.Y * TileWeatherComponent.ChunkSize);

        if (!TryComp<TileWeatherComponent>(gridUid, out var tileWeather))
            return;

        if (ent.Comp.Disable)
        {
            // Was disabling - clear disable flag
            if (tileWeather.Data.TryGetValue(chunkIndices, out var chunkData))
            {
                chunkData &= ~bitFlag;

                if (chunkData == 0)
                    tileWeather.Data.Remove(chunkIndices);
                else
                    tileWeather.Data[chunkIndices] = chunkData;

                Dirty(gridUid, tileWeather);
            }
        }
        else
        {
            // Was enabling - clear enable flag
            if (tileWeather.EnableData.TryGetValue(chunkIndices, out var enableData))
            {
                enableData &= ~bitFlag;

                if (enableData == 0)
                    tileWeather.EnableData.Remove(chunkIndices);
                else
                    tileWeather.EnableData[chunkIndices] = enableData;

                Dirty(gridUid, tileWeather);
            }
        }
    }
}
