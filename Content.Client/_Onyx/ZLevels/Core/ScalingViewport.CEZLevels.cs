/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Collections.Generic;
using System.Numerics;
using Content.Client._Onyx.ZLevels.Core;
using Content.Shared._Onyx.ZLevels.Core.Components;
using Content.Shared._Onyx.ZLevels.Core.EntitySystems;
using Content.Shared._Utopia.ZLevels.Components;
using Content.Shared.CCVar;
using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client.Viewport;

public sealed partial class ScalingViewport
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly ITileDefinitionManager _tile = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private CEClientZLevelsSystem? _zLevels;

    private EntityQuery<TransformComponent>? _xformQuery;
    private EntityQuery<MapComponent>? _mapQuery;

    private IEye? _fallbackEye;

    private readonly Dictionary<int, (EntityUid MapUid, uint Frame)> _depthMapCache = new();
    private uint _depthMapCacheFrame;
    private bool _renderingNonBaseZLayer;
    private bool _lowerDepthCacheValid;
    private bool _drawLowerZCacheThisFrame;
    private int _lowerDepthCacheFrameCounter;
    private readonly List<Entity<MapGridComponent>> _visibleGridsBuffer = new();
    private const float EmptyTileCacheLifetime = 0.5f;
    private const int LowerDepthCacheFrameInterval = 1;
    private readonly Dictionary<(EntityUid MapUid, int CellX, int CellY, int Radius), bool> _emptyTileCache = new();
    private TimeSpan _emptyTileCacheExpiry;
    private TimeSpan _forceRenderBelowUntil = TimeSpan.Zero;
    private bool _zLevelCvarsSubscribed;
    private int _cachedMaxZLevelsBelowRendering;
    private float _cachedZLevelOffset;
    private int _cachedLowerRenderRadius;
    private readonly ZEye _sharedZEye = new();
    private static readonly Color TransparentColor = new(0f, 0f, 0f, 0f);
    private const int MinZLevelsBelowRendering = 0;
    private const int MaxZLevelsBelowRendering = 3;
    private static readonly TimeSpan LinkedGridLowerRenderGrace = TimeSpan.FromSeconds(0.25f);
    private bool GetViewportScanCached(
        EntityUid mapUid,
        Vector2 centerPosition,
        float searchRadius)
    {
        if (_timing.CurTime >= _emptyTileCacheExpiry)
        {
            _emptyTileCache.Clear();
            _emptyTileCacheExpiry = _timing.CurTime + TimeSpan.FromSeconds(EmptyTileCacheLifetime);
        }

        var radius = searchRadius > 0f ? (int) MathF.Ceiling(searchRadius) : 0;
        var cacheKey = radius > 0
            ? (mapUid, (int) MathF.Floor(centerPosition.X), (int) MathF.Floor(centerPosition.Y), radius)
            : (mapUid, int.MinValue, int.MinValue, 0);

        if (_emptyTileCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var result = ScanVisibleTiles(mapUid, centerPosition, searchRadius);
        _emptyTileCache[cacheKey] = result;
        return result;
    }

    private bool TryFindEmptyTilesCached(EntityUid mapUid, Vector2 centerPosition, float searchRadius)
    {
        return GetViewportScanCached(mapUid, centerPosition, searchRadius);
    }

    /// <summary>
    /// We are looking for at least one empty tile on the screen.
    /// This is used to ensure that it makes sense to draw the z-planes and that they are visible.
    /// </summary>
    public bool TryFindEmptyTiles(EntityUid mapUid, Vector2 centerPosition, float searchRadius)
    {
        return ScanVisibleTiles(mapUid, centerPosition, searchRadius);
    }

    private bool CheckSamplePoint(EntityUid mapUid, Vector2 sample, MapId mapId)
    {
        if (!_mapManager.TryFindGridAt(mapUid, sample, out _, out var sampleGrid))
            return true;

        var sampleTile = sampleGrid.GetTileRef(sampleGrid.TileIndicesFor(new MapCoordinates(sample, mapId)));
        if (sampleTile.Tile.IsEmpty)
            return true;

        var sampleTileDef = (ContentTileDefinition)_tile[sampleTile.Tile.TypeId];
        return sampleTileDef.Transparent;
    }

    private bool ScanVisibleTiles(
        EntityUid mapUid,
        Vector2 centerPosition,
        float searchRadius)
    {
        if (_xformQuery is null || !_xformQuery.Value.TryComp(mapUid, out var xform))
            return true;

        var drawBox = GetDrawBox();
        var mapId = xform.MapID;

        var bl = _eyeManager.ScreenToMap(drawBox.BottomLeft).Position;
        var br = _eyeManager.ScreenToMap(drawBox.BottomRight).Position;
        var tl = _eyeManager.ScreenToMap(drawBox.TopLeft).Position;
        var tr = _eyeManager.ScreenToMap(drawBox.TopRight).Position;

        var minX = MathF.Min(MathF.Min(bl.X, br.X), MathF.Min(tl.X, tr.X));
        var minY = MathF.Min(MathF.Min(bl.Y, br.Y), MathF.Min(tl.Y, tr.Y));
        var maxX = MathF.Max(MathF.Max(bl.X, br.X), MathF.Max(tl.X, tr.X));
        var maxY = MathF.Max(MathF.Max(bl.Y, br.Y), MathF.Max(tl.Y, tr.Y));

        if (searchRadius > 0f)
        {
            minX = MathF.Max(minX, centerPosition.X - searchRadius);
            minY = MathF.Max(minY, centerPosition.Y - searchRadius);
            maxX = MathF.Min(maxX, centerPosition.X + searchRadius);
            maxY = MathF.Min(maxY, centerPosition.Y + searchRadius);

            if (minX >= maxX || minY >= maxY)
                return false;
        }

        var center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

        if (CheckSamplePoint(mapUid, new Vector2(minX, minY), mapId)
            || CheckSamplePoint(mapUid, new Vector2(maxX, minY), mapId)
            || CheckSamplePoint(mapUid, new Vector2(minX, maxY), mapId)
            || CheckSamplePoint(mapUid, new Vector2(maxX, maxY), mapId)
            || CheckSamplePoint(mapUid, center, mapId))
            return true;

        var worldBounds = new Box2(minX, minY, maxX, maxY);
        var mapCoordsBottomLeft = new MapCoordinates(new Vector2(minX, minY), mapId);
        var mapCoordsTopRight = new MapCoordinates(new Vector2(maxX, maxY), mapId);

        var visibleGrids = _visibleGridsBuffer;
        visibleGrids.Clear();
        _mapManager.FindGridsIntersecting(mapId, worldBounds, ref visibleGrids, approx: true, includeMap: false);

        if (visibleGrids.Count == 0)
            return true;

        foreach (var grid in visibleGrids)
        {
            var mapGrid = grid.Comp;
            var tileBottomLeft = mapGrid.TileIndicesFor(mapCoordsBottomLeft);
            var tileTopRight = mapGrid.TileIndicesFor(mapCoordsTopRight);

            for (var x = tileBottomLeft.X - 1; x <= tileTopRight.X + 1; x++)
            {
                for (var y = tileBottomLeft.Y - 1; y <= tileTopRight.Y + 1; y++)
                {
                    var pos = new Vector2i(x, y);
                    var tile = mapGrid.GetTileRef(pos);
                    var isOpen = tile.Tile.IsEmpty;

                    if (!isOpen)
                    {
                        var tileDef = (ContentTileDefinition) _tile[tile.Tile.TypeId];
                        isOpen = tileDef.Transparent;
                    }

                    if (!isOpen)
                        continue;

                    return true;
                }
            }
        }

        return false;
    }

    private void RenderZLevels(IClydeViewport viewport)
    {
        if (_eye is null)
            return;

        _fallbackEye = _eye;

        // Cache frequently accessed components/systems
        _xformQuery ??= _entityManager.GetEntityQuery<TransformComponent>();
        _mapQuery ??= _entityManager.GetEntityQuery<MapComponent>();

        // Cache systems and components
        _zLevels ??= _entityManager.System<CEClientZLevelsSystem>();
        EnsureZLevelCvarCache();

        if (_player.LocalEntity is null)
            return;

        if (!_entityManager.TryGetComponent<CEZLevelViewerComponent>(_player.LocalEntity.Value, out var zLevelViewer))
            return;

        if (!_xformQuery.Value.TryComp(_player.LocalEntity, out var playerXform))
            return;

        if (playerXform.MapUid is null)
            return;

        AdvanceDepthMapCacheFrame();
        _depthMapCache[0] = (playerXform.MapUid.Value, _depthMapCacheFrame);

        var lookUp = zLevelViewer.LookUp ? 1 : 0;

        var maxBelow = _cachedMaxZLevelsBelowRendering;
        var lowerRenderSearchRadius = GetEffectiveLowerRenderRadius();
        var playerPosition = playerXform.Coordinates.Position;
        var currentMapHasOpenTiles = GetViewportScanCached(
            playerXform.MapUid.Value,
            playerPosition,
            lowerRenderSearchRadius);

        var onLinkedGrid = playerXform.GridUid.HasValue
                           && _entityManager.HasComponent<GridMotionLinkComponent>(playerXform.GridUid.Value);

        if (onLinkedGrid && currentMapHasOpenTiles)
            _forceRenderBelowUntil = _timing.CurTime + LinkedGridLowerRenderGrace;

        var forceRenderBelow = onLinkedGrid && _timing.CurTime < _forceRenderBelowUntil;
        var lowestDepth = 0;
        if (maxBelow > 0 && (currentMapHasOpenTiles || forceRenderBelow))
        {
            for (var i = 0; i >= -maxBelow; i--)
            {
                var checkingMap = playerXform.MapUid.Value;

                if (i != 0)
                {
                    if (!_zLevels.TryMapOffset(playerXform.MapUid.Value, i, out var mapUidBelow))
                        continue;

                    checkingMap = mapUidBelow.Value;
                }

                lowestDepth = i;

                if (!forceRenderBelow)
                {
                    var hasOpenTiles = i == 0
                        ? currentMapHasOpenTiles
                        : GetViewportScanCached(checkingMap, playerPosition, lowerRenderSearchRadius);

                    if (!hasOpenTiles)
                        break;
                }
            }
        }

        var zLevelOffset = _cachedZLevelOffset;
        Angle rotation = _fallbackEye.Rotation * -1;
        var rotationVector = rotation.ToWorldVec();
        _renderingNonBaseZLayer = false;
        try
        {
            var hasLowerDepths = lowestDepth < 0;
            if (hasLowerDepths && _lowerZViewport != null)
            {
                _lowerDepthCacheFrameCounter++;
                var refreshLowerCache = !_lowerDepthCacheValid || _lowerDepthCacheFrameCounter >= LowerDepthCacheFrameInterval;
                if (refreshLowerCache)
                {
                    _lowerDepthCacheFrameCounter = 0;
                    _lowerDepthCacheValid = RenderDepthRange(
                        _lowerZViewport,
                        playerXform,
                        lowestDepth,
                        -1,
                        lowestDepth,
                        -1,
                        rotationVector,
                        zLevelOffset,
                        Color.Black);
                }

                _drawLowerZCacheThisFrame = _lowerDepthCacheValid;
            }
            else
            {
                _lowerDepthCacheValid = false;
                _drawLowerZCacheThisFrame = false;
                _lowerDepthCacheFrameCounter = 0;
            }

            var firstBaseClear = _drawLowerZCacheThisFrame ? TransparentColor : Color.Black;
            RenderDepthRange(
                viewport,
                playerXform,
                lowestDepth,
                lookUp,
                0,
                lookUp,
                rotationVector,
                zLevelOffset,
                firstBaseClear);
        }
        finally
        {
            _renderingNonBaseZLayer = false;
            // Restore the Eye
            Eye = _fallbackEye;
            viewport.Eye = _fallbackEye;
            if (_lowerZViewport != null)
                _lowerZViewport.Eye = _fallbackEye;
        }
    }

    private bool RenderDepthRange(
        IClydeViewport targetViewport,
        TransformComponent playerXform,
        int lowestDepth,
        int highestDepth,
        int fromDepth,
        int toDepth,
        Vector2 rotationVector,
        float zLevelOffset,
        Color? firstClearColor)
    {
        if (fromDepth > toDepth)
            return false;

        var renderedAny = false;
        for (var depth = fromDepth; depth <= toDepth; depth++)
        {
            if (depth == 0)
            {
                _renderingNonBaseZLayer = false;
                targetViewport.Eye = _fallbackEye;
            }
            else
            {
                if (!TryGetDepthMapCached(depth, out var depthMapUid))
                {
                    if (!_zLevels!.TryMapOffset(playerXform.MapUid!.Value, depth, out var mapUidDepth))
                        continue;

                    depthMapUid = mapUidDepth.Value;
                    _depthMapCache[depth] = (depthMapUid, _depthMapCacheFrame);
                }

                if (!_mapQuery!.Value.TryComp(depthMapUid, out var mapComp))
                    continue;

                var offset = rotationVector * zLevelOffset * depth;
                _sharedZEye.LowestDepth = lowestDepth;
                _sharedZEye.Depth = depth;
                _sharedZEye.HighestDepth = highestDepth;
                _sharedZEye.Position = new MapCoordinates(_fallbackEye!.Position.Position, mapComp.MapId);
                _sharedZEye.DrawFov = _fallbackEye.DrawFov && depth >= 0;
                _sharedZEye.DrawLight = _fallbackEye.DrawLight;
                _sharedZEye.Offset = _fallbackEye.Offset + offset;
                _sharedZEye.Rotation = _fallbackEye.Rotation;
                _sharedZEye.Scale = _fallbackEye.Scale;

                _renderingNonBaseZLayer = true;
                targetViewport.Eye = _sharedZEye;
            }

            targetViewport.ClearColor = renderedAny ? null : firstClearColor;
            targetViewport.Render();
            renderedAny = true;
        }

        return renderedAny;
    }

    private UIBox2i GetLowerZScreenRect(UIBox2i drawBox)
    {
        return drawBox;
    }

    private void AdvanceDepthMapCacheFrame()
    {
        _depthMapCacheFrame++;
        if (_depthMapCacheFrame != 0)
            return;

        _depthMapCacheFrame = 1;
        _depthMapCache.Clear();
    }

    private bool TryGetDepthMapCached(int depth, out EntityUid mapUid)
    {
        if (_depthMapCache.TryGetValue(depth, out var entry) && entry.Frame == _depthMapCacheFrame)
        {
            mapUid = entry.MapUid;
            return true;
        }

        mapUid = default;
        return false;
    }

    private void EnsureZLevelCvarCache()
    {
        if (_zLevelCvarsSubscribed)
            return;

        _cfg.OnValueChanged(CCVars.MaxZLevelsBelowRendering, value =>
        {
            _cachedMaxZLevelsBelowRendering = Math.Clamp(value, MinZLevelsBelowRendering, MaxZLevelsBelowRendering);
        }, true);
        _cfg.OnValueChanged(CCVars.ZLevelOffset, value => _cachedZLevelOffset = value, true);
        _cfg.OnValueChanged(CCVars.ZLevelLowerRenderProbeRadius, value =>
        {
            _cachedLowerRenderRadius = Math.Max(0, value);
            _emptyTileCache.Clear();
        }, true);
        _zLevelCvarsSubscribed = true;
    }

    private float GetEffectiveLowerRenderRadius()
    {
        var radius = Math.Max(0, _cachedLowerRenderRadius);
        if (radius == 0)
            return 0f;

        return radius;
    }

    public sealed class ZEye : Robust.Shared.Graphics.Eye
    {
        public int LowestDepth;
        public int Depth;
        public int HighestDepth;
    }
}
