using System.Numerics;
using System.Collections.Generic;
using Content.Client.Viewport;
using Content.Shared.CCVar;
using Content.Shared._CE.ZLevels.Core.Components;
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

    private readonly SharedMapSystem _mapSystem;
    private readonly SharedTransformSystem _xform;
    private readonly ZLevelOverlayCache _cache;

    private EntityQuery<CEZLevelMapComponent> _zMapQuery;
    private EntityQuery<GridMotionLinkComponent> _motionLinkQuery;

    private List<Entity<MapGridComponent>> _lowerGrids = new();
    private readonly Dictionary<EntityUid, List<RowRun>> _cachedShadowRuns = new();
    private readonly List<EntityUid> _staleLowerRunCacheKeys = new();
    private readonly HashSet<Vector2i> _projectedHoleTiles = new();
    private readonly Dictionary<int, List<int>> _batchedRows = new();
    private readonly List<int> _usedBatchRows = new();
    private TimeSpan _nextShadowRebuild;

    private float _cachedOpacity;
    private Color _cachedShadowBaseColor = Color.Black;
    private Color _cachedFillColor = Color.Black;
    private bool _shadowEnabled = true;
    private int _shadowUpdateRate = 20;
    private float _shadowMaxDistance;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public OnyxZLevelHoleShadowOverlay()
    {
        IoCManager.InjectDependencies(this);

        _mapSystem = _entManager.System<SharedMapSystem>();
        _xform = _entManager.System<SharedTransformSystem>();
        _cache = _entManager.System<ZLevelOverlayCache>();

        _zMapQuery = _entManager.GetEntityQuery<CEZLevelMapComponent>();
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
        if (!_cache.HasUpperMap)
            return;

        if (!TryGetMapUid(args.MapId, out var lowerMapUid) || !_zMapQuery.HasComp(lowerMapUid))
            return;

        _lowerGrids.Clear();
        _mapManager.FindGridsIntersecting(args.MapId, args.WorldBounds, ref _lowerGrids, approx: true, includeMap: false);

        if (_lowerGrids.Count == 0)
            return;

        _cache.RebuildUpperGridGroups(args.WorldBounds);

        if (_timing.CurTime >= _nextShadowRebuild)
        {
            RebuildVisibleShadowRuns(args);
            _nextShadowRebuild = _timing.CurTime + TimeSpan.FromSeconds(1f / _shadowUpdateRate);
        }

        var worldHandle = args.WorldHandle;
        var holeShadowFillColor = _cachedFillColor;

        foreach (var lowerGrid in _lowerGrids)
        {
            if (!_cachedShadowRuns.TryGetValue(lowerGrid.Owner, out var runs) || runs.Count == 0)
                continue;

            worldHandle.SetTransform(_xform.GetWorldMatrix(lowerGrid.Owner));
            foreach (var run in runs)
            {
                DrawHorizontalRun(worldHandle, lowerGrid.Comp.TileSize, run.Y, run.StartX, run.EndX, holeShadowFillColor);
            }
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
    }

    private void RebuildVisibleShadowRuns(in OverlayDrawArgs args)
    {
        var eyePosition = args.Viewport.Eye?.Position.Position ?? Vector2.Zero;
        var limitByDistance = _shadowMaxDistance > 0f;
        var maxDistanceSquared = _shadowMaxDistance * _shadowMaxDistance;

        foreach (var lowerGrid in _lowerGrids)
        {
            var runs = GetOrCreateShadowRuns(lowerGrid.Owner);
            runs.Clear();

            _projectedHoleTiles.Clear();
            if (!_motionLinkQuery.TryComp(lowerGrid.Owner, out var lowerLink) || string.IsNullOrEmpty(lowerLink.GroupId))
                continue;

            if (!_cache.TryGetUpperGridsForGroup(lowerLink.GroupId, out var linkedUpperGrids))
                continue;

            foreach (var upperGrid in linkedUpperGrids)
            {
                _projectedHoleTiles.UnionWith(_cache.GetProjectedHoleTiles(lowerGrid, upperGrid));
            }

            if (_projectedHoleTiles.Count == 0)
                continue;

            GetVisibleTileBounds(lowerGrid, args.MapId, args.WorldBounds, out var minX, out var maxX, out var minY, out var maxY);
            BuildBatchedRows(lowerGrid, minX, maxX, minY, maxY, eyePosition, limitByDistance, maxDistanceSquared);
            AppendBatchedRuns(runs);
        }
    }

    private List<RowRun> GetOrCreateShadowRuns(EntityUid lowerGridUid)
    {
        if (_cachedShadowRuns.TryGetValue(lowerGridUid, out var runs))
            return runs;

        runs = new List<RowRun>();
        _cachedShadowRuns[lowerGridUid] = runs;
        return runs;
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

    private void BuildBatchedRows(
        Entity<MapGridComponent> lowerGrid,
        int minX, int maxX, int minY, int maxY,
        Vector2 eyePosition, bool limitByDistance, float maxDistanceSquared)
    {
        foreach (var row in _usedBatchRows)
            _batchedRows[row].Clear();
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

    private void OnShadowUpdateRateChanged(int updateRate)
    {
        _shadowUpdateRate = Math.Clamp(updateRate, 1, 60);
        _nextShadowRebuild = TimeSpan.Zero;
    }

    private void OnShadowMaxDistanceChanged(float maxDistance)
    {
        _shadowMaxDistance = MathF.Max(0f, maxDistance);
        _nextShadowRebuild = TimeSpan.Zero;
    }

    private static void DrawHorizontalRun(DrawingHandleWorld worldHandle, ushort tileSize, int y, int startX, int endX, Color fillColor)
    {
        var min = new Vector2(startX * tileSize, y * tileSize);
        var max = new Vector2((endX + 1) * tileSize, (y + 1) * tileSize);
        worldHandle.DrawRect(new Box2(min, max), fillColor);
    }

    private readonly record struct RowRun(int Y, int StartX, int EndX);
}
