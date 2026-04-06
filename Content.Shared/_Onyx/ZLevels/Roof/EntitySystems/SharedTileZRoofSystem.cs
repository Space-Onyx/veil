using System.Collections.Generic;
using Content.Shared._Onyx.ZLevels.Roof.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Shared._Onyx.ZLevels.Roof.EntitySystems;

public class SharedTileZRoofSystem : EntitySystem
{
    public byte GetHasZRoofOverride(Entity<MapGridComponent, TileZRoofComponent> grid, Vector2i index)
    {
        var data = grid.Comp2;
        var chunkOrigin = SharedMapSystem.GetChunkIndices(index, TileZRoofComponent.ChunkSize);
        var chunkRelative = SharedMapSystem.GetChunkRelative(index, TileZRoofComponent.ChunkSize);
        var bitFlag = (ulong) 1 << (chunkRelative.X + chunkRelative.Y * TileZRoofComponent.ChunkSize);

        if (data.EnableData.TryGetValue(chunkOrigin, out var enableData) &&
            (enableData & bitFlag) == bitFlag)
        {
            return 2;
        }

        if (data.DisableData.TryGetValue(chunkOrigin, out var disableData) &&
            (disableData & bitFlag) == bitFlag)
        {
            return 1;
        }

        return 0;
    }

    public bool HasZRoof(Entity<MapGridComponent> grid, Vector2i index, bool defaultValue)
    {
        if (!TryComp<TileZRoofComponent>(grid.Owner, out var overrideComp))
            return defaultValue;

        return GetHasZRoofOverride((grid.Owner, grid.Comp, overrideComp), index) switch
        {
            1 => false,
            2 => true,
            _ => defaultValue
        };
    }

    public void SetHasZRoofOverride(Entity<MapGridComponent?, TileZRoofComponent?> grid, Vector2i index, byte value)
    {
        if (!Resolve(grid, ref grid.Comp1, ref grid.Comp2, false))
            return;

        var data = grid.Comp2;
        var chunkOrigin = SharedMapSystem.GetChunkIndices(index, TileZRoofComponent.ChunkSize);
        var chunkRelative = SharedMapSystem.GetChunkRelative(index, TileZRoofComponent.ChunkSize);
        var bitFlag = (ulong) 1 << (chunkRelative.X + chunkRelative.Y * TileZRoofComponent.ChunkSize);

        if (value == 2)
        {
            SetBit(data.EnableData, chunkOrigin, bitFlag, true);
            SetBit(data.DisableData, chunkOrigin, bitFlag, false);
        }
        else if (value == 1)
        {
            SetBit(data.EnableData, chunkOrigin, bitFlag, false);
            SetBit(data.DisableData, chunkOrigin, bitFlag, true);
        }
        else
        {
            SetBit(data.EnableData, chunkOrigin, bitFlag, false);
            SetBit(data.DisableData, chunkOrigin, bitFlag, false);
        }

        Dirty(grid.Owner, data);
    }

    private static void SetBit(Dictionary<Vector2i, ulong> chunks, Vector2i chunkOrigin, ulong bitFlag, bool set)
    {
        chunks.TryGetValue(chunkOrigin, out var value);

        if (set)
            value |= bitFlag;
        else
            value &= ~bitFlag;

        if (value == 0)
            chunks.Remove(chunkOrigin);
        else
            chunks[chunkOrigin] = value;
    }
}
