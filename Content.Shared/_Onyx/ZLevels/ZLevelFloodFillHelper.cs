using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Shared._Onyx.ZLevels;

public static class ZLevelFloodFillHelper
{
    public static HashSet<Vector2i> FindInteriorHoles(
        SharedMapSystem mapSystem,
        Entity<MapGridComponent> grid,
        ITileDefinitionManager tileDef)
    {
        var solidTiles = new HashSet<Vector2i>();

        var enumerator = mapSystem.GetAllTilesEnumerator(grid.Owner, grid.Comp, ignoreEmpty: true);
        while (enumerator.MoveNext(out var tileRef))
        {
            var def = (ContentTileDefinition) tileDef[tileRef.Value.Tile.TypeId];
            if (!def.MapAtmosphere)
                solidTiles.Add(tileRef.Value.GridIndices);
        }

        return FindInteriorHolesFromSolid(solidTiles);
    }

    public static HashSet<Vector2i> FindInteriorHoles(
        SharedMapSystem mapSystem,
        Entity<MapGridComponent> grid)
    {
        var solidTiles = new HashSet<Vector2i>();

        var enumerator = mapSystem.GetAllTilesEnumerator(grid.Owner, grid.Comp, ignoreEmpty: true);
        while (enumerator.MoveNext(out var tileRef))
        {
            solidTiles.Add(tileRef.Value.GridIndices);
        }

        return FindInteriorHolesFromSolid(solidTiles);
    }

    public static HashSet<Vector2i> FindInteriorHolesFromSolid(HashSet<Vector2i> solidTiles)
    {
        var holes = new HashSet<Vector2i>();

        if (solidTiles.Count == 0)
            return holes;

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;

        foreach (var pos in solidTiles)
        {
            if (pos.X < minX) minX = pos.X;
            if (pos.Y < minY) minY = pos.Y;
            if (pos.X > maxX) maxX = pos.X;
            if (pos.Y > maxY) maxY = pos.Y;
        }

        minX--;
        minY--;
        maxX++;
        maxY++;

        var outerEmpty = new HashSet<Vector2i>();
        var queue = new Queue<Vector2i>();

        void TryEnqueue(Vector2i p)
        {
            if (solidTiles.Contains(p))
                return;
            if (!outerEmpty.Add(p))
                return;
            queue.Enqueue(p);
        }

        for (var x = minX; x <= maxX; x++)
        {
            TryEnqueue(new Vector2i(x, minY));
            TryEnqueue(new Vector2i(x, maxY));
        }

        for (var y = minY + 1; y < maxY; y++)
        {
            TryEnqueue(new Vector2i(minX, y));
            TryEnqueue(new Vector2i(maxX, y));
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.X < minX || current.X > maxX || current.Y < minY || current.Y > maxY)
                continue;
            TryEnqueue(new Vector2i(current.X + 1, current.Y));
            TryEnqueue(new Vector2i(current.X - 1, current.Y));
            TryEnqueue(new Vector2i(current.X, current.Y + 1));
            TryEnqueue(new Vector2i(current.X, current.Y - 1));
        }

        for (var x = minX + 1; x < maxX; x++)
        {
            for (var y = minY + 1; y < maxY; y++)
            {
                var pos = new Vector2i(x, y);
                if (solidTiles.Contains(pos))
                    continue;
                if (outerEmpty.Contains(pos))
                    continue;

                holes.Add(pos);
            }
        }

        return holes;
    }
}
