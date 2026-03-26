using Content.Server._Onyx.Weather.Components;
using Content.Shared._Onyx.Weather.Components;
using Content.Shared._Onyx.Weather.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Onyx.Wether.EntitySystems;

public sealed partial class TileWeatherServerSystem : SharedTileWeatherSystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private EntityQuery<MapGridComponent> _gridQuery;

    public override void Initialize()
    {
        base.Initialize();

        _gridQuery = GetEntityQuery<MapGridComponent>();

        SubscribeLocalEvent<SetTileWeatherComponent, ComponentStartup>(OnStartup);

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
        RemComp<TileWeatherComponent>(ev.EntityUid);
    }

    private void OnStartup(Entity<SetTileWeatherComponent> ent, ref ComponentStartup args)
    {
        TryApply(ent);
        QueueDel(ent.Owner);
    }

    private void TryApply(Entity<SetTileWeatherComponent> ent)
    {
        var xform = Transform(ent.Owner);
        EntityUid? gridUid = null;
        if (xform.GridUid != null && _gridQuery.HasComp(xform.GridUid.Value))
            gridUid = xform.GridUid.Value;
        else if (_gridQuery.HasComp(xform.ParentUid))
            gridUid = xform.ParentUid;

        if (gridUid == null || !_gridQuery.TryComp(gridUid.Value, out var grid))
            return;

        var tileWeather = EnsureComp<TileWeatherComponent>(gridUid.Value);
        var tileIndices = _map.LocalToTile(gridUid.Value, grid, xform.Coordinates);
        var chunkIndices = SharedMapSystem.GetChunkIndices(tileIndices, TileWeatherComponent.ChunkSize);
        var chunkRelative = SharedMapSystem.GetChunkRelative(tileIndices, TileWeatherComponent.ChunkSize);
        var bitFlag = (ulong) 1 << (chunkRelative.X + chunkRelative.Y * TileWeatherComponent.ChunkSize);

        if (ent.Comp.Disable)
        {
            // Disable weather for this tile.
            if (tileWeather.EnableData.TryGetValue(chunkIndices, out var enableData))
            {
                enableData &= ~bitFlag;
                if (enableData == 0)
                    tileWeather.EnableData.Remove(chunkIndices);
                else
                    tileWeather.EnableData[chunkIndices] = enableData;
            }

            if (!tileWeather.Data.TryGetValue(chunkIndices, out var disableData))
                disableData = 0;

            disableData |= bitFlag;
            tileWeather.Data[chunkIndices] = disableData;
        }
        else
        {
            // Enable weather for this tile.
            if (tileWeather.Data.TryGetValue(chunkIndices, out var disableData))
            {
                disableData &= ~bitFlag;
                if (disableData == 0)
                    tileWeather.Data.Remove(chunkIndices);
                else
                    tileWeather.Data[chunkIndices] = disableData;
            }

            if (!tileWeather.EnableData.TryGetValue(chunkIndices, out var enableData))
                enableData = 0;

            enableData |= bitFlag;
            tileWeather.EnableData[chunkIndices] = enableData;
        }

        Dirty(gridUid.Value, tileWeather);
    }
}
