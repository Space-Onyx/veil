using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Shared._Onyx.ZLevels;

public static class ZLevelFloodFillHelper
{
    private static readonly object PoolLock = new();
    private static readonly Stack<HashSet<Vector2i>> SolidSetPool = new();
    private static readonly Stack<HashSet<Vector2i>> OuterSetPool = new();
    private static readonly Stack<Queue<Vector2i>> QueuePool = new();
    private const int MaxPoolSize = 8;

    private static HashSet<Vector2i> RentSet(Stack<HashSet<Vector2i>> pool)
    {
        lock (PoolLock)
        {
            if (pool.Count > 0)
                return pool.Pop();
        }

        return new HashSet<Vector2i>();
    }

    private static void ReturnSet(Stack<HashSet<Vector2i>> pool, HashSet<Vector2i> set)
    {
        set.Clear();

        lock (PoolLock)
        {
            if (pool.Count < MaxPoolSize)
                pool.Push(set);
        }
    }

    private static Queue<Vector2i> RentQueue()
    {
        lock (PoolLock)
        {
            if (QueuePool.Count > 0)
                return QueuePool.Pop();
        }

        return new Queue<Vector2i>();
    }

    private static void ReturnQueue(Queue<Vector2i> queue)
    {
        queue.Clear();

        lock (PoolLock)
        {
            if (QueuePool.Count < MaxPoolSize)
                QueuePool.Push(queue);
        }
    }

    public static HashSet<Vector2i> FindInteriorHoles(
        SharedMapSystem mapSystem,
        Entity<MapGridComponent> grid,
        ITileDefinitionManager tileDef)
    {
        var solidTiles = RentSet(SolidSetPool);
        try
        {
            var enumerator = mapSystem.GetAllTilesEnumerator(grid.Owner, grid.Comp, ignoreEmpty: true);
            while (enumerator.MoveNext(out var tileRef))
            {
                var def = (ContentTileDefinition) tileDef[tileRef.Value.Tile.TypeId];
                if (!def.MapAtmosphere)
                    solidTiles.Add(tileRef.Value.GridIndices);
            }

            return FindInteriorHolesFromSolid(solidTiles);
        }
        finally
        {
            ReturnSet(SolidSetPool, solidTiles);
        }
    }

    public static HashSet<Vector2i> FindInteriorHoles(
        SharedMapSystem mapSystem,
        Entity<MapGridComponent> grid)
    {
        var solidTiles = RentSet(SolidSetPool);
        try
        {
            var enumerator = mapSystem.GetAllTilesEnumerator(grid.Owner, grid.Comp, ignoreEmpty: true);
            while (enumerator.MoveNext(out var tileRef))
            {
                solidTiles.Add(tileRef.Value.GridIndices);
            }

            return FindInteriorHolesFromSolid(solidTiles);
        }
        finally
        {
            ReturnSet(SolidSetPool, solidTiles);
        }
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

        var outerEmpty = RentSet(OuterSetPool);
        var queue = RentQueue();

        try
        {
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
        }
        finally
        {
            ReturnSet(OuterSetPool, outerEmpty);
            ReturnQueue(queue);
        }

        return holes;
    }
}
