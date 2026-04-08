using System.Collections.Generic;
using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._Onyx.ZLevels;

public enum ZLevelProjectionKind : byte
{
    RoofMask = 0,
    InteriorHoles = 1,
}

public sealed class OnyxZLevelProjectionCacheSystem : EntitySystem
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedTransformSystem _xformSystem = default!;

    private readonly Dictionary<ProjectionKey, ProjectionCacheEntry> _pairProjectionCache = new();
    private readonly Queue<ProjectionKey> _pairProjectionCleanupQueue = new();
    private static readonly TimeSpan CacheCleanupInterval = TimeSpan.FromSeconds(5);
    private const int ProjectionCleanupBudget = 64;
    private TimeSpan _nextCacheCleanup;

    public HashSet<Vector2i> GetProjectedTiles(
        Entity<MapGridComponent> lowerGrid,
        Entity<MapGridComponent> upperGrid,
        ZLevelProjectionKind projectionKind,
        HashSet<Vector2i> upperTiles)
    {
        if (_timing.CurTime >= _nextCacheCleanup)
        {
            CleanupCaches();
            _nextCacheCleanup = _timing.CurTime + CacheCleanupInterval;
        }

        var key = new ProjectionKey(lowerGrid.Owner, upperGrid.Owner, projectionKind);
        var upperTileTick = upperGrid.Comp.LastTileModifiedTick;
        var lowerMatrix = _xformSystem.GetWorldMatrix(lowerGrid.Owner);
        var upperMatrix = _xformSystem.GetWorldMatrix(upperGrid.Owner);

        var hadCachedProjection = _pairProjectionCache.TryGetValue(key, out var cachedProjection);
        if (hadCachedProjection
            && cachedProjection.UpperTileTick == upperTileTick
            && cachedProjection.LowerMatrix == lowerMatrix
            && cachedProjection.UpperMatrix == upperMatrix)
        {
            return cachedProjection.Tiles;
        }

        HashSet<Vector2i> projected;
        if (cachedProjection.Tiles != null)
        {
            projected = cachedProjection.Tiles;
            projected.Clear();
        }
        else
        {
            projected = new HashSet<Vector2i>(Math.Max(upperTiles.Count, 4));
        }

        foreach (var pos in upperTiles)
        {
            var worldPos = _mapSystem.GridTileToWorldPos(upperGrid.Owner, upperGrid.Comp, pos);
            var lowerTilePos = _mapSystem.WorldToTile(lowerGrid.Owner, lowerGrid.Comp, worldPos);
            projected.Add(lowerTilePos);
        }

        _pairProjectionCache[key] = new ProjectionCacheEntry(upperTileTick, lowerMatrix, upperMatrix, projected);
        if (!hadCachedProjection)
            _pairProjectionCleanupQueue.Enqueue(key);
        return projected;
    }

    private void CleanupCaches()
    {
        if (_pairProjectionCleanupQueue.Count == 0)
            return;

        var checks = Math.Min(ProjectionCleanupBudget, _pairProjectionCleanupQueue.Count);
        for (var i = 0; i < checks; i++)
        {
            var key = _pairProjectionCleanupQueue.Dequeue();

            if (!_pairProjectionCache.ContainsKey(key))
                continue;

            if (_entManager.EntityExists(key.LowerGrid) && _entManager.EntityExists(key.UpperGrid))
            {
                _pairProjectionCleanupQueue.Enqueue(key);
                continue;
            }

            _pairProjectionCache.Remove(key);
        }
    }

    private readonly record struct ProjectionKey(EntityUid LowerGrid, EntityUid UpperGrid, ZLevelProjectionKind ProjectionKind);
    private readonly record struct ProjectionCacheEntry(GameTick UpperTileTick, Matrix3x2 LowerMatrix, Matrix3x2 UpperMatrix, HashSet<Vector2i> Tiles);
}
