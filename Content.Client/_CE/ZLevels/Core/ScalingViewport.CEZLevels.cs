/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Collections.Generic;
using System.Numerics;
using Content.Client._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
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
    [Dependency] private readonly IConfigurationManager _cfg = default!; // <Onyx-Tweak>
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IOverlayManager _overlayManager = default!; // <Onyx-Tweak>

    private CEClientZLevelsSystem? _zLevels;
    private SharedMapSystem? _mapSystem;

    private EntityQuery<TransformComponent>? _xformQuery;
    private EntityQuery<MapComponent>? _mapQuery;

    private IEye? _fallbackEye;
    // <Onyx-Tweak> 
    private readonly Dictionary<int, EntityUid> _depthMapCache = new();

    private Overlay? _cachedPlacementOverlay;
    private bool _placementOverlayCached;
    private readonly List<Entity<MapGridComponent>> _visibleGridsBuffer = new();
    private const float EmptyTileCacheLifetime = 0.5f;
    private readonly Dictionary<EntityUid, bool> _emptyTileCache = new();
    private TimeSpan _emptyTileCacheExpiry;
    // </Onyx-Tweak>

    private bool TryFindEmptyTilesCached(EntityUid mapUid)
    {
        if (_timing.CurTime >= _emptyTileCacheExpiry)
        {
            _emptyTileCache.Clear();
            _emptyTileCacheExpiry = _timing.CurTime + TimeSpan.FromSeconds(EmptyTileCacheLifetime);
        }

        if (_emptyTileCache.TryGetValue(mapUid, out var cached))
            return cached;

        var result = TryFindEmptyTiles(mapUid);
        _emptyTileCache[mapUid] = result;
        return result;
    }

    /// <summary>
    /// We are looking for at least one empty tile on the screen.
    /// This is used to ensure that it makes sense to draw the z-planes and that they are visible.
    /// </summary>
    public bool TryFindEmptyTiles(EntityUid mapUid)
    {
        if (_xformQuery is null || !_xformQuery.Value.TryComp(mapUid, out var xform))
            return true;

        var drawBox = GetDrawBox();
        var mapId = xform.MapID;

        // <Onyx-Tweak Edited>
        var bl = _eyeManager.ScreenToMap(drawBox.BottomLeft).Position;
        var br = _eyeManager.ScreenToMap(drawBox.BottomRight).Position;
        var tl = _eyeManager.ScreenToMap(drawBox.TopLeft).Position;
        var tr = _eyeManager.ScreenToMap(drawBox.TopRight).Position;

        var minX = MathF.Min(MathF.Min(bl.X, br.X), MathF.Min(tl.X, tr.X));
        var minY = MathF.Min(MathF.Min(bl.Y, br.Y), MathF.Min(tl.Y, tr.Y));
        var maxX = MathF.Max(MathF.Max(bl.X, br.X), MathF.Max(tl.X, tr.X));
        var maxY = MathF.Max(MathF.Max(bl.Y, br.Y), MathF.Max(tl.Y, tr.Y));
        // </Onyx-Tweak Edited>

        // Handle gaps between disconnected grids: if any important screen point is not covered by a solid tile,
        // we should render lower z-levels.
        // <Onyx-Tweak Edited>
        if (CheckSamplePoint(mapUid, new Vector2(minX, minY), mapId)
            || CheckSamplePoint(mapUid, new Vector2(maxX, minY), mapId)
            || CheckSamplePoint(mapUid, new Vector2(minX, maxY), mapId)
            || CheckSamplePoint(mapUid, new Vector2(maxX, maxY), mapId)
            || CheckSamplePoint(mapUid, new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f), mapId))
            return true;

        var worldBounds = new Box2(minX, minY, maxX, maxY);
        var mapCoordsBottomLeft = new MapCoordinates(new Vector2(minX, minY), mapId);
        var mapCoordsTopRight = new MapCoordinates(new Vector2(maxX, maxY), mapId);

        var visibleGrids = _visibleGridsBuffer;
        visibleGrids.Clear();
        _mapManager.FindGridsIntersecting(mapId, worldBounds, ref visibleGrids, approx: true, includeMap: false);
        // </Onyx-Tweak Edited>

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
                    var tile = mapGrid.GetTileRef(new Vector2i(x, y));
                    // <Onyx-Tweak Edited>
                    if (tile.Tile.IsEmpty)
                        return true;

                    var tileDef = (ContentTileDefinition)_tile[tile.Tile.TypeId];
                    if (tileDef.Transparent)
                        return true;
                    // </Onyx-Tweak Edited>
                }
            }
        }

        return false;
    }

    // <Onyx-Tweak>
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
    // </Onyx-Tweak>

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
        _mapSystem ??= _entityManager.System<SharedMapSystem>();

        if (_player.LocalEntity is null)
            return;

        if (!_entityManager.TryGetComponent<CEZLevelViewerComponent>(_player.LocalEntity.Value, out var zLevelViewer))
            return;

        if (!_xformQuery.Value.TryComp(_player.LocalEntity, out var playerXform))
            return;

        if (playerXform.MapUid is null)
            return;

        // <Onyx-Tweak> 
        _depthMapCache.Clear();
        _depthMapCache[0] = playerXform.MapUid.Value;
        // </Onyx-Tweak> 

        var lookUp = zLevelViewer.LookUp ? 1 : 0;

        var forceRenderBelow = playerXform.GridUid.HasValue
            && _entityManager.HasComponent<GridMotionLinkComponent>(playerXform.GridUid.Value);

        var maxBelow = _cfg.GetCVar(CCVars.MaxZLevelsBelowRendering); // <Onyx-Tweak>
        var lowestDepth = 0;
        for (var i = 0; i >= -maxBelow; i--)   // <Onyx-Tweak>
        {
            var checkingMap = playerXform.MapUid.Value;

            if (i != 0)
            {
                if (!_zLevels.TryMapOffset(playerXform.MapUid.Value, i, out var mapUidBelow))
                    continue;

                checkingMap = mapUidBelow.Value;
                _depthMapCache[i] = checkingMap; // <Onyx-Tweak> 
            }

            lowestDepth = i;

            if (!forceRenderBelow && !TryFindEmptyTilesCached(checkingMap)) // <Onyx-Tweak Edited>
                break;
        }


        // <Onyx-Tweak>
        if (!_placementOverlayCached)
        {
            _cachedPlacementOverlay = null;
            foreach (var overlay in _overlayManager.AllOverlays) // <Onyx-Tweak Edited>
            {
                if (overlay.GetType().Name == "PlacementOverlay")
                {
                    _cachedPlacementOverlay = overlay;
                    break;
                }
            }
            _placementOverlayCached = true;
        }
        // </Onyx-Tweak>

        var placementOverlay = _cachedPlacementOverlay;
        var placementRemoved = false;

        var zLevelOffset = _cfg.GetCVar(CCVars.ZLevelOffset);

        //From the lowest depth to the highest, render each level
        for (var depth = lowestDepth; depth <= lookUp; depth++)
        {
            if (depth == 0)
            {
                viewport.Eye = _fallbackEye;

                // Restore placement overlay for the base layer
                if (placementRemoved && placementOverlay is not null)
                {
                    try
                    {
                        _overlayManager.AddOverlay(placementOverlay); // <Onyx-Tweak Edited>
                        placementRemoved = false;
                    }
                    catch { }
                }
            }
            else
            {
                // <Onyx-Tweak> 
                if (!_depthMapCache.TryGetValue(depth, out var depthMapUid))
                {
                    if (!_zLevels.TryMapOffset(playerXform.MapUid.Value, depth, out var mapUidDepth))
                        continue;

                    depthMapUid = mapUidDepth.Value;
                    _depthMapCache[depth] = depthMapUid;
                }

                if (!_mapQuery.Value.TryComp(depthMapUid, out var mapComp))
                    continue;
                // </Onyx-Tweak> 

                // Remove placement overlay before rendering z-levels so it
                // doesn't call PixelToMap with this z-level's eye.
                if (!placementRemoved && placementOverlay is not null)
                {
                    try
                    {
                        _overlayManager.RemoveOverlay(placementOverlay); // <Onyx-Tweak Edited>
                        placementRemoved = true;
                    }
                    catch { }
                }

                Angle rotation = _fallbackEye.Rotation * -1;
                var offset = rotation.ToWorldVec() * zLevelOffset * depth; // <Onyx-Tweak Edited>

                viewport.Eye = new ZEye(lowestDepth, depth, lookUp)
                {
                    Position = new MapCoordinates(_fallbackEye.Position.Position, mapComp.MapId),
                    DrawFov = _fallbackEye.DrawFov && depth >= 0,
                    DrawLight = _fallbackEye.DrawLight,
                    Offset = _fallbackEye.Offset + offset,
                    Rotation = _fallbackEye.Rotation,
                    Scale = _fallbackEye.Scale,
                };
            }

            viewport.ClearColor = depth == lowestDepth ? Color.Black : null;
            viewport.Render();
        }


        // Ensure placement overlay is restored
        if (placementRemoved && placementOverlay is not null)
        {
            try
            {
                _overlayManager.AddOverlay(placementOverlay); // <Onyx-Tweak Edited>
            }
            catch { }
        }

        // Restore the Eye
        Eye = _fallbackEye;
        viewport.Eye = _fallbackEye;
    }

    public sealed class ZEye(int lowest, int depth, int high) : Robust.Shared.Graphics.Eye
    {
        public int LowestDepth = lowest;
        public int Depth = depth;
        public int HighestDepth = high;
    }
}
