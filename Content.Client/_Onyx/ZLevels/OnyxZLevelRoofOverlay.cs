using System.Numerics;
using Content.Client.Viewport;
using Content.Shared.CCVar;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._CE.ZLevels.Roof.EntitySystems;
using Content.Shared._Utopia.ZLevels.Components;
using Robust.Shared.Configuration;
using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Client._Onyx.ZLevels;

public sealed class OnyxZLevelRoofOverlay : Overlay
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDef = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly SharedMapSystem _mapSystem;
    private readonly SharedTransformSystem _xformSystem;
    private readonly CESharedZLevelsSystem _zLevels;
    private readonly SharedTileZRoofSystem _tileZRoof;

    private EntityQuery<GridMotionLinkComponent> _motionLinkQuery;
    private EntityQuery<CEZLevelMapComponent> _zMapQuery;
    private EntityQuery<MapComponent> _mapQuery;

    private List<Entity<MapGridComponent>> _lowerGrids = new();
    private List<Entity<MapGridComponent>> _upperGridsBuffer = new();
    private readonly Dictionary<string, List<Entity<MapGridComponent>>> _upperGridsByGroup = new();
    private readonly List<string> _usedUpperGroupKeys = new();
    private readonly HashSet<string> _visibleLowerGroups = new();
    private readonly HashSet<Vector2i> _excludedTiles = new();
    private readonly Dictionary<EntityUid, UpperMaskCacheEntry> _upperMaskCache = new();
    private readonly OnyxZLevelProjectionCacheSystem _projectionCache;
    private readonly List<EntityUid> _staleUpperMaskKeys = new();
    private static readonly TimeSpan CacheCleanupInterval = TimeSpan.FromSeconds(5);
    private TimeSpan _nextCacheCleanup;
    private readonly Dictionary<int, List<int>> _batchedRows = new();
    private readonly List<int> _usedBatchRows = new();
    private bool _roofOverlayEnabled = true;

    private static readonly Color RoofColor = new(0.1f, 0.1f, 0.1f, 1.0f);

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public OnyxZLevelRoofOverlay()
    {
        IoCManager.InjectDependencies(this);

        _mapSystem = _entManager.System<SharedMapSystem>();
        _xformSystem = _entManager.System<SharedTransformSystem>();
        _zLevels = _entManager.System<CESharedZLevelsSystem>();
        _tileZRoof = _entManager.System<SharedTileZRoofSystem>();
        _projectionCache = _entManager.System<OnyxZLevelProjectionCacheSystem>();

        _motionLinkQuery = _entManager.GetEntityQuery<GridMotionLinkComponent>();
        _zMapQuery = _entManager.GetEntityQuery<CEZLevelMapComponent>();
        _mapQuery = _entManager.GetEntityQuery<MapComponent>();

        ZIndex = 1;

        _cfg.OnValueChanged(CCVars.ZLevelRoofOverlayEnabled, value => _roofOverlayEnabled = value, true);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_roofOverlayEnabled)
            return false;

        if (args.Viewport.Eye is not ScalingViewport.ZEye zeye)
            return false;

        if (zeye.Depth >= 0)
            return false;

        if (args.MapId == MapId.Nullspace)
            return false;

        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not ScalingViewport.ZEye)
            return;

        var worldHandle = args.WorldHandle;
        var bounds = args.WorldBounds;
        var lowerMapId = args.MapId;

        if (!TryGetMapUid(lowerMapId, out var lowerMapUid) || !_zMapQuery.HasComp(lowerMapUid))
            return;

        MapId? upperMapId = null;
        if (_zLevels.TryMapUp(lowerMapUid, out var upperMapEntity) &&
            _mapQuery.TryComp(upperMapEntity.Value, out var upperMapComp))
        {
            upperMapId = upperMapComp.MapId;
        }

        _lowerGrids.Clear();
        _mapManager.FindGridsIntersecting(lowerMapId, bounds, ref _lowerGrids, approx: true, includeMap: false);

        if (_lowerGrids.Count == 0)
            return;

        _visibleLowerGroups.Clear();
        foreach (var lowerGrid in _lowerGrids)
        {
            if (!_motionLinkQuery.TryComp(lowerGrid.Owner, out var lowerLink) || string.IsNullOrEmpty(lowerLink.GroupId))
                continue;

            _visibleLowerGroups.Add(lowerLink.GroupId);
        }

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
        if (upperMapId != null && _visibleLowerGroups.Count > 0)
        {
            _upperGridsBuffer.Clear();
            _mapManager.FindGridsIntersecting(upperMapId.Value, bounds, ref _upperGridsBuffer, approx: true, includeMap: false);

            foreach (var upperGrid in _upperGridsBuffer)
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
        }

        foreach (var lowerGrid in _lowerGrids)
        {
            _excludedTiles.Clear();
            if (_motionLinkQuery.TryComp(lowerGrid.Owner, out var lowerLink)
                && !string.IsNullOrEmpty(lowerLink.GroupId)
                && _upperGridsByGroup.TryGetValue(lowerLink.GroupId, out var linkedUpperGrids))
            {
                foreach (var upperGrid in linkedUpperGrids)
                {
                    _excludedTiles.UnionWith(GetProjectedExclusionTiles(lowerGrid, upperGrid));
                }
            }

            var gridMatrix = _xformSystem.GetWorldMatrix(lowerGrid.Owner);
            worldHandle.SetTransform(gridMatrix);
            ClearBatchedRows();

            var tileEnumerator = _mapSystem.GetTilesEnumerator(lowerGrid.Owner, lowerGrid, bounds);
            while (tileEnumerator.MoveNext(out var tileRef))
            {
                if (tileRef.Tile.IsEmpty)
                    continue;

                var def = (ContentTileDefinition) _tileDef[tileRef.Tile.TypeId];
                if (!_tileZRoof.HasZRoof(lowerGrid, tileRef.GridIndices, def.HasZRoof))
                    continue;

                if (_excludedTiles.Contains(tileRef.GridIndices))
                    continue;

                AddBatchedTile(tileRef.GridIndices.X, tileRef.GridIndices.Y);
            }

            DrawBatchedRows(worldHandle, lowerGrid.Comp.TileSize, RoofColor);
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
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

    private HashSet<Vector2i> GetProjectedExclusionTiles(Entity<MapGridComponent> lowerGrid, Entity<MapGridComponent> upperGrid)
    {
        var upperMask = GetUpperMaskTiles(upperGrid);
        return _projectionCache.GetProjectedTiles(lowerGrid, upperGrid, ZLevelProjectionKind.RoofMask, upperMask);
    }

    private HashSet<Vector2i> GetUpperMaskTiles(Entity<MapGridComponent> upperGrid)
    {
        var tileTick = upperGrid.Comp.LastTileModifiedTick;
        if (_upperMaskCache.TryGetValue(upperGrid.Owner, out var cachedMask)
            && cachedMask.TileTick == tileTick)
        {
            return cachedMask.Tiles;
        }

        var solidTiles = new HashSet<Vector2i>();
        var maskTiles = new HashSet<Vector2i>();
        var enumerator = _mapSystem.GetAllTilesEnumerator(upperGrid.Owner, upperGrid.Comp, ignoreEmpty: true);
        while (enumerator.MoveNext(out var upperTileRef))
        {
            var gridPos = upperTileRef.Value.GridIndices;
            var def = (ContentTileDefinition)_tileDef[upperTileRef.Value.Tile.TypeId];
            if (def.MapAtmosphere)
                continue;

            solidTiles.Add(gridPos);
            maskTiles.Add(gridPos);
        }

        if (solidTiles.Count > 0)
            maskTiles.UnionWith(ZLevelFloodFillHelper.FindInteriorHolesFromSolid(solidTiles));

        _upperMaskCache[upperGrid.Owner] = new UpperMaskCacheEntry(tileTick, maskTiles);
        return maskTiles;
    }

    private void CleanupCaches()
    {
        _staleUpperMaskKeys.Clear();
        foreach (var gridUid in _upperMaskCache.Keys)
        {
            if (_entManager.EntityExists(gridUid))
                continue;

            _staleUpperMaskKeys.Add(gridUid);
        }

        foreach (var key in _staleUpperMaskKeys)
        {
            _upperMaskCache.Remove(key);
        }
    }

    private void ClearBatchedRows()
    {
        foreach (var y in _usedBatchRows)
        {
            _batchedRows[y].Clear();
        }

        _usedBatchRows.Clear();
    }

    private void AddBatchedTile(int x, int y)
    {
        if (!_batchedRows.TryGetValue(y, out var row))
        {
            row = new List<int>();
            _batchedRows[y] = row;
        }

        if (row.Count == 0)
            _usedBatchRows.Add(y);

        row.Add(x);
    }

    private void DrawBatchedRows(DrawingHandleWorld worldHandle, ushort tileSize, Color color)
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

                DrawHorizontalRun(worldHandle, tileSize, y, runStart, runEnd, color);
                runStart = x;
                runEnd = x;
            }

            DrawHorizontalRun(worldHandle, tileSize, y, runStart, runEnd, color);
        }
    }

    private static void DrawHorizontalRun(DrawingHandleWorld worldHandle, ushort tileSize, int y, int startX, int endX, Color color)
    {
        var min = new Vector2(startX * tileSize, y * tileSize);
        var max = new Vector2((endX + 1) * tileSize, (y + 1) * tileSize);
        worldHandle.DrawRect(new Box2(min, max), color);
    }

    private readonly record struct UpperMaskCacheEntry(GameTick TileTick, HashSet<Vector2i> Tiles);
}
