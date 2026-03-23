using System.Collections.Generic;
using System.Numerics;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._Utopia.ZLevels.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Client._Onyx.ZLevels;

public sealed class ZLevelOverlayCache : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDef = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private SharedMapSystem _mapSystem = default!;
    private SharedTransformSystem _xformSystem = default!;
    private CESharedZLevelsSystem _zLevels = default!;

    private EntityQuery<CEZLevelMapComponent> _zMapQuery;
    private EntityQuery<MapComponent> _mapQuery;
    private EntityQuery<GridMotionLinkComponent> _motionLinkQuery;
    private EntityQuery<TransformComponent> _xformQuery;
    private readonly Dictionary<EntityUid, InteriorHoleCacheEntry> _interiorHoleCache = new();
    private readonly Dictionary<(EntityUid Lower, EntityUid Upper), ProjectionCacheEntry> _holeProjCache = new();
    private readonly Dictionary<EntityUid, UpperMaskCacheEntry> _upperMaskCache = new();
    private readonly Dictionary<(EntityUid Lower, EntityUid Upper), ProjectionCacheEntry> _maskProjCache = new();
    private readonly Dictionary<string, List<Entity<MapGridComponent>>> _upperGridsByGroup = new();
    private readonly List<string> _usedGroupKeys = new();
    private MapId _currentUpperMapId;
    private bool _hasUpperMap;
    private EntityUid _currentLowerMapUid;
    private bool _frameValid;
    private uint _upperGridsRebuildTick;
    private readonly List<EntityUid> _staleKeys = new();
    private readonly List<(EntityUid, EntityUid)> _stalePairKeys = new();
    private static readonly TimeSpan CacheCleanupInterval = TimeSpan.FromSeconds(5);
    private TimeSpan _nextCleanup;

    public override void Initialize()
    {
        base.Initialize();
        _mapSystem = EntityManager.System<SharedMapSystem>();
        _xformSystem = EntityManager.System<SharedTransformSystem>();
        _zLevels = EntityManager.System<CESharedZLevelsSystem>();

        _zMapQuery = GetEntityQuery<CEZLevelMapComponent>();
        _mapQuery = GetEntityQuery<MapComponent>();
        _motionLinkQuery = GetEntityQuery<GridMotionLinkComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
    }

    public override void FrameUpdate(float frameTime)
    {
        _frameValid = false;

        if (_timing.CurTime >= _nextCleanup)
        {
            CleanupStaleEntries();
            _nextCleanup = _timing.CurTime + CacheCleanupInterval;
        }

        if (_player.LocalEntity is not { } localEnt)
            return;

        if (!_xformQuery.TryComp(localEnt, out var playerXform) || playerXform.MapUid is not { } mapUid)
            return;

        if (!_zMapQuery.HasComp(mapUid))
            return;

        _currentLowerMapUid = mapUid;

        if (_zLevels.TryMapUp(mapUid, out var upperMapEnt) && _mapQuery.TryComp(upperMapEnt.Value, out var upperMapComp))
        {
            _currentUpperMapId = upperMapComp.MapId;
            _hasUpperMap = true;
        }
        else
        {
            _hasUpperMap = false;
        }

        _frameValid = true;
    }

    public bool HasUpperMap => _frameValid && _hasUpperMap;
    public MapId UpperMapId => _currentUpperMapId;
    public EntityUid LowerMapUid => _currentLowerMapUid;
    public bool TryGetUpperGridsForGroup(string groupId, out List<Entity<MapGridComponent>> grids)
    {
        if (_upperGridsByGroup.TryGetValue(groupId, out grids!) && grids.Count > 0)
            return true;

        grids = default!;
        return false;
    }
    public void RebuildUpperGridGroups(Box2Rotated bounds)
    {
        var curTick = _timing.CurTick.Value;
        if (_upperGridsRebuildTick == curTick)
            return;
        _upperGridsRebuildTick = curTick;

        foreach (var key in _usedGroupKeys)
            _upperGridsByGroup[key].Clear();
        _usedGroupKeys.Clear();

        if (!_hasUpperMap)
            return;

        var upperGrids = new List<Entity<MapGridComponent>>();
        _mapManager.FindGridsIntersecting(_currentUpperMapId, bounds, ref upperGrids, approx: true, includeMap: false);

        foreach (var upperGrid in upperGrids)
        {
            if (!_motionLinkQuery.TryComp(upperGrid.Owner, out var link) || string.IsNullOrEmpty(link.GroupId))
                continue;

            if (!_upperGridsByGroup.TryGetValue(link.GroupId, out var list))
            {
                list = new List<Entity<MapGridComponent>>();
                _upperGridsByGroup[link.GroupId] = list;
            }

            if (list.Count == 0)
                _usedGroupKeys.Add(link.GroupId);

            list.Add(upperGrid);
        }
    }

    public HashSet<Vector2i> GetInteriorHoles(Entity<MapGridComponent> upperGrid)
    {
        var tileTick = upperGrid.Comp.LastTileModifiedTick;
        if (_interiorHoleCache.TryGetValue(upperGrid.Owner, out var cached) && cached.TileTick == tileTick)
            return cached.Holes;

        var holes = Content.Shared._Onyx.ZLevels.ZLevelFloodFillHelper.FindInteriorHoles(_mapSystem, upperGrid, _tileDef);
        _interiorHoleCache[upperGrid.Owner] = new InteriorHoleCacheEntry(tileTick, holes);
        return holes;
    }

    public HashSet<Vector2i> GetProjectedHoleTiles(Entity<MapGridComponent> lowerGrid, Entity<MapGridComponent> upperGrid)
    {
        var key = (lowerGrid.Owner, upperGrid.Owner);
        var tileTick = upperGrid.Comp.LastTileModifiedTick;
        var lowerMatrix = _xformSystem.GetWorldMatrix(lowerGrid.Owner);
        var upperMatrix = _xformSystem.GetWorldMatrix(upperGrid.Owner);

        if (_holeProjCache.TryGetValue(key, out var cached)
            && cached.UpperTileTick == tileTick
            && cached.LowerMatrix == lowerMatrix
            && cached.UpperMatrix == upperMatrix)
        {
            return cached.Tiles;
        }

        var upperHoles = GetInteriorHoles(upperGrid);

        HashSet<Vector2i> projected;
        if (_holeProjCache.TryGetValue(key, out var old))
        {
            projected = old.Tiles;
            projected.Clear();
        }
        else
        {
            projected = new HashSet<Vector2i>();
        }

        foreach (var pos in upperHoles)
        {
            var worldPos = _mapSystem.GridTileToWorldPos(upperGrid.Owner, upperGrid.Comp, pos);
            var lowerTilePos = _mapSystem.WorldToTile(lowerGrid.Owner, lowerGrid.Comp, worldPos);
            projected.Add(lowerTilePos);
        }

        _holeProjCache[key] = new ProjectionCacheEntry(tileTick, lowerMatrix, upperMatrix, projected);
        return projected;
    }
    public HashSet<Vector2i> GetProjectedMaskTiles(Entity<MapGridComponent> lowerGrid, Entity<MapGridComponent> upperGrid)
    {
        var key = (lowerGrid.Owner, upperGrid.Owner);
        var tileTick = upperGrid.Comp.LastTileModifiedTick;
        var lowerMatrix = _xformSystem.GetWorldMatrix(lowerGrid.Owner);
        var upperMatrix = _xformSystem.GetWorldMatrix(upperGrid.Owner);

        if (_maskProjCache.TryGetValue(key, out var cached)
            && cached.UpperTileTick == tileTick
            && cached.LowerMatrix == lowerMatrix
            && cached.UpperMatrix == upperMatrix)
        {
            return cached.Tiles;
        }

        var upperMask = GetUpperMaskTiles(upperGrid);

        HashSet<Vector2i> projected;
        if (_maskProjCache.TryGetValue(key, out var old))
        {
            projected = old.Tiles;
            projected.Clear();
        }
        else
        {
            projected = new HashSet<Vector2i>(upperMask.Count);
        }

        foreach (var pos in upperMask)
        {
            var worldPos = _mapSystem.GridTileToWorldPos(upperGrid.Owner, upperGrid.Comp, pos);
            var lowerTilePos = _mapSystem.WorldToTile(lowerGrid.Owner, lowerGrid.Comp, worldPos);
            projected.Add(lowerTilePos);
        }

        _maskProjCache[key] = new ProjectionCacheEntry(tileTick, lowerMatrix, upperMatrix, projected);
        return projected;
    }

    private HashSet<Vector2i> GetUpperMaskTiles(Entity<MapGridComponent> upperGrid)
    {
        var tileTick = upperGrid.Comp.LastTileModifiedTick;
        if (_upperMaskCache.TryGetValue(upperGrid.Owner, out var cached) && cached.TileTick == tileTick)
            return cached.Tiles;

        var solidTiles = new HashSet<Vector2i>();
        var maskTiles = new HashSet<Vector2i>();
        var enumerator = _mapSystem.GetAllTilesEnumerator(upperGrid.Owner, upperGrid.Comp, ignoreEmpty: true);
        while (enumerator.MoveNext(out var tileRef))
        {
            var def = (Content.Shared.Maps.ContentTileDefinition) _tileDef[tileRef.Value.Tile.TypeId];
            if (def.MapAtmosphere)
                continue;

            solidTiles.Add(tileRef.Value.GridIndices);
            maskTiles.Add(tileRef.Value.GridIndices);
        }

        if (solidTiles.Count > 0)
            maskTiles.UnionWith(Content.Shared._Onyx.ZLevels.ZLevelFloodFillHelper.FindInteriorHolesFromSolid(solidTiles));

        _upperMaskCache[upperGrid.Owner] = new UpperMaskCacheEntry(tileTick, maskTiles);
        return maskTiles;
    }

    private void CleanupStaleEntries()
    {
        _staleKeys.Clear();
        foreach (var uid in _interiorHoleCache.Keys)
        {
            if (!EntityManager.EntityExists(uid))
                _staleKeys.Add(uid);
        }
        foreach (var key in _staleKeys)
            _interiorHoleCache.Remove(key);

        _staleKeys.Clear();
        foreach (var uid in _upperMaskCache.Keys)
        {
            if (!EntityManager.EntityExists(uid))
                _staleKeys.Add(uid);
        }
        foreach (var key in _staleKeys)
            _upperMaskCache.Remove(key);

        _stalePairKeys.Clear();
        foreach (var key in _holeProjCache.Keys)
        {
            if (!EntityManager.EntityExists(key.Lower) || !EntityManager.EntityExists(key.Upper))
                _stalePairKeys.Add(key);
        }
        foreach (var key in _stalePairKeys)
            _holeProjCache.Remove(key);

        _stalePairKeys.Clear();
        foreach (var key in _maskProjCache.Keys)
        {
            if (!EntityManager.EntityExists(key.Lower) || !EntityManager.EntityExists(key.Upper))
                _stalePairKeys.Add(key);
        }
        foreach (var key in _stalePairKeys)
            _maskProjCache.Remove(key);
    }

    private readonly record struct InteriorHoleCacheEntry(GameTick TileTick, HashSet<Vector2i> Holes);
    private readonly record struct UpperMaskCacheEntry(GameTick TileTick, HashSet<Vector2i> Tiles);
    private readonly record struct ProjectionCacheEntry(GameTick UpperTileTick, Matrix3x2 LowerMatrix, Matrix3x2 UpperMatrix, HashSet<Vector2i> Tiles);
}