using Content.Shared._Vortex.Weather.Components;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._Vortex.Weather.EntitySystems;

public abstract class SharedTileWeatherSystem : EntitySystem
{
    /// <summary>
    /// Returns the weather override value for the specified tile.
    /// 0 = default (use tile setting), 1 = weather disabled, 2 = weather enabled.
    /// </summary>
    public byte GetWeatherOverride(Entity<MapGridComponent, TileWeatherComponent> grid, Vector2i index)
    {
        var weather = grid.Comp2;
        var chunkOrigin = SharedMapSystem.GetChunkIndices(index, TileWeatherComponent.ChunkSize);

        if (weather.Data.TryGetValue(chunkOrigin, out var chunkData))
        {
            var chunkRelative = SharedMapSystem.GetChunkRelative(index, TileWeatherComponent.ChunkSize);
            var bitFlag = (ulong) 1 << (chunkRelative.X + chunkRelative.Y * TileWeatherComponent.ChunkSize);
            if (weather.EnableData.TryGetValue(chunkOrigin, out var enableData))
            {
                if ((enableData & bitFlag) == bitFlag)
                    return 2;
            }

            if ((chunkData & bitFlag) == bitFlag)
                return 1;
        }

        return 0;
    }

    /// <summary>
    /// Sets the weather override flag on a tile.
    /// </summary>
    public void SetWeatherOverride(Entity<MapGridComponent?, TileWeatherComponent?> grid, Vector2i index, byte value)
    {
        if (!Resolve(grid, ref grid.Comp1, ref grid.Comp2, false))
            return;

        var chunkOrigin = SharedMapSystem.GetChunkIndices(index, TileWeatherComponent.ChunkSize);
        var weather = grid.Comp2;

        var chunkRelative = SharedMapSystem.GetChunkRelative(index, TileWeatherComponent.ChunkSize);
        var bitFlag = (ulong) 1 << (chunkRelative.X + chunkRelative.Y * TileWeatherComponent.ChunkSize);

        if (value == 1)
        {
            if (!weather.Data.TryGetValue(chunkOrigin, out var chunkData))
            {
                chunkData = 0;
            }

            if ((chunkData & bitFlag) == bitFlag)
                return;

            chunkData |= bitFlag;
            weather.Data[chunkOrigin] = chunkData;
            Dirty(grid.Owner, weather);
        }
        else if (value == 2) 
        {
            if (!weather.EnableData.TryGetValue(chunkOrigin, out var enableData))
            {
                enableData = 0;
            }   

            if ((enableData & bitFlag) == bitFlag)
                return;

            enableData |= bitFlag;
            weather.EnableData[chunkOrigin] = enableData;
            Dirty(grid.Owner, weather);
        }
    }
}
