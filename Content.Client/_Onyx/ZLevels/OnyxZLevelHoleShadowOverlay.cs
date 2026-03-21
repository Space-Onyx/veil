using System.Numerics;
using System.Collections.Generic;
using Content.Client.Viewport;
using Content.Shared.CCVar;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._Utopia.ZLevels.Components;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._Onyx.ZLevels;

public sealed class OnyxZLevelHoleShadowOverlay : Overlay
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDef = default!;

    private readonly CESharedZLevelsSystem _zLevels;
    private readonly SharedMapSystem _mapSystem;
    private readonly SharedTransformSystem _xform;

    private EntityQuery<CEZLevelMapComponent> _zMapQuery;
    private EntityQuery<MapComponent> _mapQuery;
    private EntityQuery<GridMotionLinkComponent> _motionLinkQuery;

    private List<Entity<MapGridComponent>> _lowerGrids = new();
    private List<Entity<MapGridComponent>> _upperGrids = new();
    private readonly Dictionary<string, List<Entity<MapGridComponent>>> _upperGridsByGroup = new();
    private readonly List<string> _usedUpperGroupKeys = new();
    private readonly HashSet<string> _visibleLowerGroups = new();
    private readonly Dictionary<EntityUid, InteriorHoleCacheEntry> _upperInteriorHoleCache = new();
    private readonly Dictionary<(EntityUid LowerGrid, EntityUid UpperGrid), ProjectionCacheEntry> _pairProjectionCache = new();
    private readonly List<EntityUid> _staleUpperGridCacheKeys = new();
    private readonly List<(EntityUid LowerGrid, EntityUid UpperGrid)> _stalePairCacheKeys = new();
    private static readonly TimeSpan CacheCleanupInterval = TimeSpan.FromSeconds(5);
    private TimeSpan _nextCacheCleanup;

    private float _cachedOpacity;
    private Color _cachedShadowBaseColor = Color.Black;
    private Color _cachedFillColor = Color.Black;

    private readonly HashSet<Vector2i> _projectedHoleTiles = new();
    private readonly Dictionary<int, List<int>> _batchedRows = new();
    private readonly List<int> _usedBatchRows = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public OnyxZLevelHoleShadowOverlay()
    {
        IoCManager.InjectDependencies(this);

        _zLevels = _entManager.System<CESharedZLevelsSystem>();
        _mapSystem = _entManager.System<SharedMapSystem>();
        _xform = _entManager.System<SharedTransformSystem>();

        _zMapQuery = _entManager.GetEntityQuery<CEZLevelMapComponent>();
        _mapQuery = _entManager.GetEntityQuery<MapComponent>();
        _motionLinkQuery = _entManager.GetEntityQuery<GridMotionLinkComponent>();

        ZIndex = 100;

        UpdateCachedColors();
        _cfg.OnValueChanged(CCVars.ZLevelHoleShadowOpacity, _ => UpdateCachedColors());
        _cfg.OnValueChanged(CCVars.ZLevelHoleShadowColor, _ => UpdateCachedColors());
    }

    private void UpdateCachedColors()
    {
        _cachedOpacity = Math.Clamp(_cfg.GetCVar(CCVars.ZLevelHoleShadowOpacity), 0f, 1f);
        _cachedShadowBaseColor = Color.FromHex(_cfg.GetCVar(CCVars.ZLevelHoleShadowColor), Color.Black);
        _cachedFillColor = _cachedShadowBaseColor.WithAlpha(1f - _cachedOpacity);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace)
            return false;

        if (args.Viewport.Eye is ScalingViewport.ZEye zeye)
            return zeye.Depth == 0;

        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!TryGetMapUid(args.MapId, out var lowerMapUid) || !_zMapQuery.HasComp(lowerMapUid))
            return;

        if (!_zLevels.TryMapUp(lowerMapUid, out var upperMapEnt))
            return;

        if (!_mapQuery.TryComp(upperMapEnt.Value, out var upperMapComp))
            return;

        var upperMapId = upperMapComp.MapId;

        _lowerGrids.Clear();
        _mapManager.FindGridsIntersecting(args.MapId, args.WorldBounds, ref _lowerGrids, approx: true, includeMap: false);

        if (_lowerGrids.Count == 0)
            return;

        _visibleLowerGroups.Clear();
        foreach (var lowerGrid in _lowerGrids)
        {
            if (!_motionLinkQuery.TryComp(lowerGrid.Owner, out var lowerLink) || string.IsNullOrEmpty(lowerLink.GroupId))
                continue;

            _visibleLowerGroups.Add(lowerLink.GroupId);
        }

        if (_visibleLowerGroups.Count == 0)
            return;

        _upperGrids.Clear();
        _mapManager.FindGridsIntersecting(upperMapId, args.WorldBounds, ref _upperGrids, approx: true, includeMap: false);

        if (_upperGrids.Count == 0)
            return;

        if (_timing.CurTime >= _nextCacheCleanup)
        {
            CleanupCaches();
            _nextCacheCleanup = _timing.CurTime + CacheCleanupInterval;
        }

        foreach (var key in _usedUpperGroupKeys)
        {
            _upperGridsByGroup[key].Clear();
        }

        _usedUpperGroupKeys.Clear();

        foreach (var upperGrid in _upperGrids)
        {
            if (!_motionLinkQuery.TryComp(upperGrid.Owner, out var upperLink) || string.IsNullOrEmpty(upperLink.GroupId))
                continue;
            if (!_visibleLowerGroups.Contains(upperLink.GroupId))
                continue;

            if (!_upperGridsByGroup.TryGetValue(upperLink.GroupId, out var groupedUpper))
            {
                groupedUpper = new List<Entity<MapGridComponent>>();
                _upperGridsByGroup[upperLink.GroupId] = groupedUpper;
            }

            if (groupedUpper.Count == 0)
                _usedUpperGroupKeys.Add(upperLink.GroupId);

            groupedUpper.Add(upperGrid);
        }

        var worldHandle = args.WorldHandle;
        var holeShadowFillColor = _cachedFillColor;

        foreach (var lowerGrid in _lowerGrids)
        {
            _projectedHoleTiles.Clear();

            if (!_motionLinkQuery.TryComp(lowerGrid.Owner, out var lowerLink) || string.IsNullOrEmpty(lowerLink.GroupId))
                continue;

            if (!_upperGridsByGroup.TryGetValue(lowerLink.GroupId, out var linkedUpperGrids))
                continue;

            foreach (var upperGrid in linkedUpperGrids)
            {
                _projectedHoleTiles.UnionWith(GetProjectedHoleTiles(lowerGrid, upperGrid));
            }

            if (_projectedHoleTiles.Count == 0)
                continue;

            worldHandle.SetTransform(_xform.GetWorldMatrix(lowerGrid.Owner));
            GetVisibleTileBounds(lowerGrid, args.MapId, args.WorldBounds, out var minX, out var maxX, out var minY, out var maxY);
            BuildBatchedRows(lowerGrid, minX, maxX, minY, maxY);
            DrawBatchedRows(worldHandle, lowerGrid.Comp.TileSize, holeShadowFillColor);
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
    }

    private HashSet<Vector2i> GetProjectedHoleTiles(Entity<MapGridComponent> lowerGrid, Entity<MapGridComponent> upperGrid)
    {
        var key = (lowerGrid.Owner, upperGrid.Owner);
        var upperTileTick = upperGrid.Comp.LastTileModifiedTick;
        var lowerMatrix = _xform.GetWorldMatrix(lowerGrid.Owner);
        var upperMatrix = _xform.GetWorldMatrix(upperGrid.Owner);
        if (_pairProjectionCache.TryGetValue(key, out var cachedProjected)
            && cachedProjected.UpperTileTick == upperTileTick
            && cachedProjected.LowerMatrix == lowerMatrix
            && cachedProjected.UpperMatrix == upperMatrix)
        {
            return cachedProjected.Tiles;
        }

        var upperHoles = GetInteriorHoleTiles(upperGrid);
        var projected = new HashSet<Vector2i>();

        if (upperHoles.Count == 0)
        {
            _pairProjectionCache[key] = new ProjectionCacheEntry(upperTileTick, lowerMatrix, upperMatrix, projected);
            return projected;
        }

        foreach (var pos in upperHoles)
        {
            var worldPos = _mapSystem.GridTileToWorldPos(upperGrid.Owner, upperGrid.Comp, pos);
            var lowerTilePos = _mapSystem.WorldToTile(lowerGrid.Owner, lowerGrid.Comp, worldPos);
            projected.Add(lowerTilePos);
        }

        _pairProjectionCache[key] = new ProjectionCacheEntry(upperTileTick, lowerMatrix, upperMatrix, projected);
        return projected;
    }

    private HashSet<Vector2i> GetInteriorHoleTiles(Entity<MapGridComponent> upperGrid)
    {
        var tileTick = upperGrid.Comp.LastTileModifiedTick;
        if (_upperInteriorHoleCache.TryGetValue(upperGrid.Owner, out var cachedHoles)
            && cachedHoles.TileTick == tileTick)
        {
            return cachedHoles.Holes;
        }

        var holes = ZLevelFloodFillHelper.FindInteriorHoles(_mapSystem, upperGrid, _tileDef);
        _upperInteriorHoleCache[upperGrid.Owner] = new InteriorHoleCacheEntry(tileTick, holes);
        return holes;
    }

    private bool TryGetMapUid(MapId mapId, out EntityUid uid)
    {
        if (_mapSystem.TryGetMap(mapId, out var mapUid))
        {
            uid = mapUid.Value;
            return true;
        }

        uid = default;
        return false;
    }

    private void GetVisibleTileBounds(Entity<MapGridComponent> lowerGrid, MapId mapId, Box2Rotated worldBounds,
        out int minX, out int maxX, out int minY, out int maxY)
    {
        var bl = lowerGrid.Comp.TileIndicesFor(new MapCoordinates(worldBounds.BottomLeft, mapId));
        var br = lowerGrid.Comp.TileIndicesFor(new MapCoordinates(worldBounds.BottomRight, mapId));
        var tl = lowerGrid.Comp.TileIndicesFor(new MapCoordinates(worldBounds.TopLeft, mapId));
        var tr = lowerGrid.Comp.TileIndicesFor(new MapCoordinates(worldBounds.TopRight, mapId));

        minX = Math.Min(Math.Min(bl.X, br.X), Math.Min(tl.X, tr.X)) - 1;
        maxX = Math.Max(Math.Max(bl.X, br.X), Math.Max(tl.X, tr.X)) + 1;
        minY = Math.Min(Math.Min(bl.Y, br.Y), Math.Min(tl.Y, tr.Y)) - 1;
        maxY = Math.Max(Math.Max(bl.Y, br.Y), Math.Max(tl.Y, tr.Y)) + 1;
    }

    private void CleanupCaches()
    {
        _staleUpperGridCacheKeys.Clear();
        foreach (var gridUid in _upperInteriorHoleCache.Keys)
        {
            if (_entManager.EntityExists(gridUid))
                continue;

            _staleUpperGridCacheKeys.Add(gridUid);
        }

        foreach (var key in _staleUpperGridCacheKeys)
        {
            _upperInteriorHoleCache.Remove(key);
        }

        _stalePairCacheKeys.Clear();
        foreach (var key in _pairProjectionCache.Keys)
        {
            if (_entManager.EntityExists(key.LowerGrid) && _entManager.EntityExists(key.UpperGrid))
                continue;

            _stalePairCacheKeys.Add(key);
        }

        foreach (var key in _stalePairCacheKeys)
        {
            _pairProjectionCache.Remove(key);
        }
    }

    private void BuildBatchedRows(Entity<MapGridComponent> lowerGrid, int minX, int maxX, int minY, int maxY)
    {
        foreach (var row in _usedBatchRows)
        {
            _batchedRows[row].Clear();
        }

        _usedBatchRows.Clear();

        foreach (var tilePos in _projectedHoleTiles)
        {
            if (tilePos.X < minX || tilePos.X > maxX || tilePos.Y < minY || tilePos.Y > maxY)
                continue;

            var tileRef = _mapSystem.GetTileRef(lowerGrid.Owner, lowerGrid.Comp, tilePos);
            if (tileRef.Tile.IsEmpty)
                continue;

            if (!_batchedRows.TryGetValue(tilePos.Y, out var row))
            {
                row = new List<int>();
                _batchedRows[tilePos.Y] = row;
            }

            if (row.Count == 0)
                _usedBatchRows.Add(tilePos.Y);

            row.Add(tilePos.X);
        }
    }

    private void DrawBatchedRows(DrawingHandleWorld worldHandle, ushort tileSize, Color fillColor)
    {
        foreach (var y in _usedBatchRows)
        {
            var row = _batchedRows[y];
            if (row.Count == 0)
                continue;

            row.Sort();

            var runStart = row[0];
            var runEnd = row[0];

            for (var i = 1; i < row.Count; i++)
            {
                var x = row[i];
                if (x <= runEnd + 1)
                {
                    runEnd = x;
                    continue;
                }

                DrawHorizontalRun(worldHandle, tileSize, y, runStart, runEnd, fillColor);
                runStart = x;
                runEnd = x;
            }

            DrawHorizontalRun(worldHandle, tileSize, y, runStart, runEnd, fillColor);
        }
    }

    private static void DrawHorizontalRun(DrawingHandleWorld worldHandle, ushort tileSize, int y, int startX, int endX, Color fillColor)
    {
        var min = new Vector2(startX * tileSize, y * tileSize);
        var max = new Vector2((endX + 1) * tileSize, (y + 1) * tileSize);
        worldHandle.DrawRect(new Box2(min, max), fillColor);
    }

    private readonly record struct InteriorHoleCacheEntry(GameTick TileTick, HashSet<Vector2i> Holes);
    private readonly record struct ProjectionCacheEntry(GameTick UpperTileTick, Matrix3x2 LowerMatrix, Matrix3x2 UpperMatrix, HashSet<Vector2i> Tiles);
}
