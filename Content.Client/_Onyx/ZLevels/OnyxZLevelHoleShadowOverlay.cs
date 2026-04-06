using System.Numerics;
using System.Collections.Generic;
using Content.Client.Viewport;
using Content.Shared.CCVar;
using Content.Shared._Onyx.ZLevels.Core.Components;
using Content.Shared._Onyx.ZLevels.Core.EntitySystems;
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
    private readonly OnyxZLevelProjectionCacheSystem _projectionCache;

    private EntityQuery<CEZLevelMapComponent> _zMapQuery;
    private EntityQuery<MapComponent> _mapQuery;
    private EntityQuery<GridMotionLinkComponent> _motionLinkQuery;

    private List<Entity<MapGridComponent>> _lowerGrids = new();
    private List<Entity<MapGridComponent>> _upperGrids = new();
    private readonly Dictionary<string, List<Entity<MapGridComponent>>> _upperGridsByGroup = new();
    private readonly Dictionary<string, ulong> _upperSignatureByGroup = new();
    private readonly List<string> _usedUpperGroupKeys = new();
    private readonly HashSet<string> _visibleLowerGroups = new();
    private readonly Dictionary<EntityUid, InteriorHoleCacheEntry> _upperInteriorHoleCache = new();
    private readonly Dictionary<EntityUid, ShadowRunCacheEntry> _cachedShadowRuns = new();
    private readonly List<EntityUid> _staleUpperGridCacheKeys = new();
    private readonly List<EntityUid> _staleLowerRunCacheKeys = new();
    private static readonly TimeSpan CacheCleanupInterval = TimeSpan.FromSeconds(5);
    private TimeSpan _nextCacheCleanup;
    private bool _forceShadowRunRebuild = true;
    private TimeSpan _shadowRunRebuildInterval = TimeSpan.Zero;
    private TimeSpan _nextShadowRunRebuild = TimeSpan.Zero;
    private MapId _lastMapId = MapId.Nullspace;

    private float _cachedOpacity;
    private Color _cachedShadowBaseColor = Color.Black;
    private Color _cachedFillColor = Color.Black;
    private bool _shadowEnabled = true;
    private float _shadowMaxDistance;

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
        _projectionCache = _entManager.System<OnyxZLevelProjectionCacheSystem>();

        _zMapQuery = _entManager.GetEntityQuery<CEZLevelMapComponent>();
        _mapQuery = _entManager.GetEntityQuery<MapComponent>();
        _motionLinkQuery = _entManager.GetEntityQuery<GridMotionLinkComponent>();

        ZIndex = 100;

        UpdateCachedColors();
        _cfg.OnValueChanged(CCVars.ZLevelHoleShadowOpacity, _ => UpdateCachedColors());
        _cfg.OnValueChanged(CCVars.ZLevelHoleShadowColor, _ => UpdateCachedColors());
        _cfg.OnValueChanged(CCVars.ZLevelHoleShadowEnabled, value => _shadowEnabled = value, true);
        _cfg.OnValueChanged(CCVars.ZLevelHoleShadowUpdateRate, OnShadowUpdateRateChanged, true);
        _cfg.OnValueChanged(CCVars.ZLevelHoleShadowMaxDistance, OnShadowMaxDistanceChanged, true);
    }

    private void UpdateCachedColors()
    {
        _cachedOpacity = Math.Clamp(_cfg.GetCVar(CCVars.ZLevelHoleShadowOpacity), 0f, 1f);
        _cachedShadowBaseColor = Color.FromHex(_cfg.GetCVar(CCVars.ZLevelHoleShadowColor), Color.Black);
        _cachedFillColor = _cachedShadowBaseColor.WithAlpha(1f - _cachedOpacity);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_shadowEnabled)
            return false;

        if (args.MapId == MapId.Nullspace)
            return false;

        if (args.Viewport.Eye is ScalingViewport.ZEye zeye)
            return zeye.Depth == 0;

        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId != _lastMapId)
        {
            _lastMapId = args.MapId;
            _forceShadowRunRebuild = true;
            _nextShadowRunRebuild = TimeSpan.Zero;
        }

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

        if (_timing.CurTime >= _nextCacheCleanup)
        {
            CleanupCaches();
            _nextCacheCleanup = _timing.CurTime + CacheCleanupInterval;
        }

        var rebuildNow = _forceShadowRunRebuild
                         || _shadowRunRebuildInterval == TimeSpan.Zero
                         || _timing.CurTime >= _nextShadowRunRebuild;

        if (rebuildNow)
        {
            RebuildVisibleShadowRuns(args, upperMapId);
            if (_shadowRunRebuildInterval > TimeSpan.Zero)
                _nextShadowRunRebuild = _timing.CurTime + _shadowRunRebuildInterval;
        }

        var worldHandle = args.WorldHandle;
        var holeShadowFillColor = _cachedFillColor;

        foreach (var lowerGrid in _lowerGrids)
        {
            if (!_cachedShadowRuns.TryGetValue(lowerGrid.Owner, out var cacheEntry) || cacheEntry.Runs.Count == 0)
                continue;

            worldHandle.SetTransform(_xform.GetWorldMatrix(lowerGrid.Owner));
            foreach (var run in cacheEntry.Runs)
            {
                DrawHorizontalRun(worldHandle, lowerGrid.Comp.TileSize, run.Y, run.StartX, run.EndX, holeShadowFillColor);
            }
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
    }

    private void RebuildVisibleShadowRuns(in OverlayDrawArgs args, MapId upperMapId)
    {
        var eyePosition = args.Viewport.Eye?.Position.Position ?? Vector2.Zero;
        var limitByDistance = _shadowMaxDistance > 0f;
        var maxDistanceSquared = _shadowMaxDistance * _shadowMaxDistance;
        var forceRebuild = _forceShadowRunRebuild;
        _forceShadowRunRebuild = false;

        _visibleLowerGroups.Clear();
        foreach (var lowerGrid in _lowerGrids)
        {
            var cacheEntry = GetOrCreateShadowRuns(lowerGrid.Owner);
            if (!_motionLinkQuery.TryComp(lowerGrid.Owner, out var lowerLink) || string.IsNullOrEmpty(lowerLink.GroupId))
            {
                cacheEntry.Runs.Clear();
                cacheEntry.Initialized = false;
                continue;
            }

            _visibleLowerGroups.Add(lowerLink.GroupId);
        }

        if (_visibleLowerGroups.Count == 0)
            return;

        _upperGrids.Clear();
        _mapManager.FindGridsIntersecting(upperMapId, args.WorldBounds, ref _upperGrids, approx: true, includeMap: false);
        if (_upperGrids.Count == 0)
        {
            foreach (var lowerGrid in _lowerGrids)
            {
                var cacheEntry = GetOrCreateShadowRuns(lowerGrid.Owner);
                cacheEntry.Runs.Clear();
                cacheEntry.Initialized = false;
            }
            return;
        }

        foreach (var key in _usedUpperGroupKeys)
        {
            _upperGridsByGroup[key].Clear();
        }

        _usedUpperGroupKeys.Clear();
        _upperSignatureByGroup.Clear();
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

        foreach (var groupKey in _usedUpperGroupKeys)
        {
            if (_upperGridsByGroup.TryGetValue(groupKey, out var linkedUpper))
                _upperSignatureByGroup[groupKey] = ComputeUpperSignature(linkedUpper);
        }

        foreach (var lowerGrid in _lowerGrids)
        {
            var cacheEntry = GetOrCreateShadowRuns(lowerGrid.Owner);
            if (!_motionLinkQuery.TryComp(lowerGrid.Owner, out var lowerLink) || string.IsNullOrEmpty(lowerLink.GroupId))
            {
                cacheEntry.Runs.Clear();
                cacheEntry.Initialized = false;
                continue;
            }

            if (!_upperGridsByGroup.TryGetValue(lowerLink.GroupId, out var linkedUpperGrids))
            {
                cacheEntry.Runs.Clear();
                cacheEntry.Initialized = false;
                continue;
            }

            GetVisibleTileBounds(lowerGrid, args.MapId, args.WorldBounds, out var minX, out var maxX, out var minY, out var maxY);
            var lowerMatrix = _xform.GetWorldMatrix(lowerGrid.Owner);
            var upperSignature = _upperSignatureByGroup.TryGetValue(lowerLink.GroupId, out var signature)
                ? signature
                : ComputeUpperSignature(linkedUpperGrids);
            var eyeTile = limitByDistance
                ? lowerGrid.Comp.TileIndicesFor(new MapCoordinates(eyePosition, args.MapId))
                : default;

            if (!forceRebuild &&
                !ShouldRebuildShadowRuns(cacheEntry,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    lowerGrid.Comp.LastTileModifiedTick,
                    lowerMatrix,
                    upperSignature,
                    limitByDistance,
                    maxDistanceSquared,
                    eyeTile))
            {
                continue;
            }

            cacheEntry.Runs.Clear();
            _projectedHoleTiles.Clear();
            foreach (var upperGrid in linkedUpperGrids)
            {
                _projectedHoleTiles.UnionWith(GetProjectedHoleTiles(lowerGrid, upperGrid));
            }

            if (_projectedHoleTiles.Count == 0)
            {
                UpdateShadowRunCacheStamp(cacheEntry,
                    minX,
                    maxX,
                    minY,
                    maxY,
                    lowerGrid.Comp.LastTileModifiedTick,
                    lowerMatrix,
                    upperSignature,
                    limitByDistance,
                    maxDistanceSquared,
                    eyeTile);
                continue;
            }

            BuildBatchedRows(lowerGrid, minX, maxX, minY, maxY, eyePosition, limitByDistance, maxDistanceSquared);
            AppendBatchedRuns(cacheEntry.Runs);

            UpdateShadowRunCacheStamp(cacheEntry,
                minX,
                maxX,
                minY,
                maxY,
                lowerGrid.Comp.LastTileModifiedTick,
                lowerMatrix,
                upperSignature,
                limitByDistance,
                maxDistanceSquared,
                eyeTile);
        }
    }

    private ShadowRunCacheEntry GetOrCreateShadowRuns(EntityUid lowerGridUid)
    {
        if (_cachedShadowRuns.TryGetValue(lowerGridUid, out var entry))
            return entry;

        entry = new ShadowRunCacheEntry();
        _cachedShadowRuns[lowerGridUid] = entry;
        return entry;
    }

    private HashSet<Vector2i> GetProjectedHoleTiles(Entity<MapGridComponent> lowerGrid, Entity<MapGridComponent> upperGrid)
    {
        var upperHoles = GetInteriorHoleTiles(upperGrid);
        return _projectionCache.GetProjectedTiles(lowerGrid, upperGrid, ZLevelProjectionKind.InteriorHoles, upperHoles);
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

        _staleLowerRunCacheKeys.Clear();
        foreach (var lowerGridUid in _cachedShadowRuns.Keys)
        {
            if (_entManager.EntityExists(lowerGridUid))
                continue;

            _staleLowerRunCacheKeys.Add(lowerGridUid);
        }

        foreach (var key in _staleLowerRunCacheKeys)
        {
            _cachedShadowRuns.Remove(key);
        }
    }

    private static bool ShouldRebuildShadowRuns(
        ShadowRunCacheEntry cache,
        int minX,
        int maxX,
        int minY,
        int maxY,
        GameTick lowerTileTick,
        Matrix3x2 lowerMatrix,
        ulong upperSignature,
        bool limitByDistance,
        float maxDistanceSquared,
        Vector2i eyeTile)
    {
        if (!cache.Initialized)
            return true;

        if (cache.MinX != minX || cache.MaxX != maxX || cache.MinY != minY || cache.MaxY != maxY)
            return true;

        if (cache.LowerTileTick != lowerTileTick || cache.LowerMatrix != lowerMatrix)
            return true;

        if (cache.UpperSignature != upperSignature)
            return true;

        if (cache.LimitByDistance != limitByDistance)
            return true;

        if (limitByDistance && (cache.EyeTile != eyeTile || MathF.Abs(cache.MaxDistanceSquared - maxDistanceSquared) > 0.001f))
            return true;

        return false;
    }

    private static void UpdateShadowRunCacheStamp(
        ShadowRunCacheEntry cache,
        int minX,
        int maxX,
        int minY,
        int maxY,
        GameTick lowerTileTick,
        Matrix3x2 lowerMatrix,
        ulong upperSignature,
        bool limitByDistance,
        float maxDistanceSquared,
        Vector2i eyeTile)
    {
        cache.MinX = minX;
        cache.MaxX = maxX;
        cache.MinY = minY;
        cache.MaxY = maxY;
        cache.LowerTileTick = lowerTileTick;
        cache.LowerMatrix = lowerMatrix;
        cache.UpperSignature = upperSignature;
        cache.LimitByDistance = limitByDistance;
        cache.MaxDistanceSquared = maxDistanceSquared;
        cache.EyeTile = eyeTile;
        cache.Initialized = true;
    }

    private ulong ComputeUpperSignature(List<Entity<MapGridComponent>> linkedUpperGrids)
    {
        var signature = (ulong) linkedUpperGrids.Count;
        foreach (var upperGrid in linkedUpperGrids)
        {
            var hash = new HashCode();
            hash.Add(upperGrid.Owner);
            hash.Add(upperGrid.Comp.LastTileModifiedTick);
            hash.Add(_xform.GetWorldMatrix(upperGrid.Owner));
            signature ^= (ulong) (uint) hash.ToHashCode();
        }

        return signature;
    }

    private void BuildBatchedRows(
        Entity<MapGridComponent> lowerGrid,
        int minX,
        int maxX,
        int minY,
        int maxY,
        Vector2 eyePosition,
        bool limitByDistance,
        float maxDistanceSquared)
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

            if (limitByDistance)
            {
                var worldPos = _mapSystem.GridTileToWorldPos(lowerGrid.Owner, lowerGrid.Comp, tilePos);
                if (Vector2.DistanceSquared(worldPos, eyePosition) > maxDistanceSquared)
                    continue;
            }

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

    private void AppendBatchedRuns(List<RowRun> runs)
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

                runs.Add(new RowRun(y, runStart, runEnd));
                runStart = x;
                runEnd = x;
            }

            runs.Add(new RowRun(y, runStart, runEnd));
        }
    }

    private void OnShadowUpdateRateChanged(int _)
    {
        var rate = _cfg.GetCVar(CCVars.ZLevelHoleShadowUpdateRate);
        _shadowRunRebuildInterval = rate <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(1f / rate);

        _forceShadowRunRebuild = true;
        _nextShadowRunRebuild = TimeSpan.Zero;
    }

    private void OnShadowMaxDistanceChanged(float maxDistance)
    {
        _shadowMaxDistance = MathF.Max(0f, maxDistance);
        _forceShadowRunRebuild = true;
    }

    private static void DrawHorizontalRun(DrawingHandleWorld worldHandle, ushort tileSize, int y, int startX, int endX, Color fillColor)
    {
        var min = new Vector2(startX * tileSize, y * tileSize);
        var max = new Vector2((endX + 1) * tileSize, (y + 1) * tileSize);
        worldHandle.DrawRect(new Box2(min, max), fillColor);
    }

    private readonly record struct InteriorHoleCacheEntry(GameTick TileTick, HashSet<Vector2i> Holes);
    private readonly record struct RowRun(int Y, int StartX, int EndX);

    private sealed class ShadowRunCacheEntry
    {
        public readonly List<RowRun> Runs = new();
        public int MinX;
        public int MaxX;
        public int MinY;
        public int MaxY;
        public GameTick LowerTileTick;
        public Matrix3x2 LowerMatrix;
        public ulong UpperSignature;
        public bool LimitByDistance;
        public float MaxDistanceSquared;
        public Vector2i EyeTile;
        public bool Initialized;
    }
}
