using System.Numerics;
using Content.Client.Viewport;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._Utopia.ZLevels.Components;
using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._Onyx.ZLevels;

public sealed class OnyxZLevelRoofOverlay : Overlay
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDef = default!;

    private readonly SharedMapSystem _mapSystem;
    private readonly SharedTransformSystem _xformSystem;
    private readonly ZLevelOverlayCache _cache;

    private EntityQuery<GridMotionLinkComponent> _motionLinkQuery;
    private EntityQuery<CEZLevelMapComponent> _zMapQuery;

    private List<Entity<MapGridComponent>> _lowerGrids = new();
    private readonly HashSet<Vector2i> _excludedTiles = new();
    private readonly Dictionary<int, List<int>> _batchedRows = new();
    private readonly List<int> _usedBatchRows = new();

    private static readonly Color RoofColor = new(0.1f, 0.1f, 0.1f, 1.0f);

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public OnyxZLevelRoofOverlay()
    {
        IoCManager.InjectDependencies(this);

        _mapSystem = _entManager.System<SharedMapSystem>();
        _xformSystem = _entManager.System<SharedTransformSystem>();
        _cache = _entManager.System<ZLevelOverlayCache>();

        _motionLinkQuery = _entManager.GetEntityQuery<GridMotionLinkComponent>();
        _zMapQuery = _entManager.GetEntityQuery<CEZLevelMapComponent>();

        ZIndex = 1;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
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

        if (!_cache.HasUpperMap)
            return;

        var worldHandle = args.WorldHandle;
        var bounds = args.WorldBounds;
        var lowerMapId = args.MapId;

        if (!TryGetMapUid(lowerMapId, out var lowerMapUid) || !_zMapQuery.HasComp(lowerMapUid))
            return;

        _lowerGrids.Clear();
        _mapManager.FindGridsIntersecting(lowerMapId, bounds, ref _lowerGrids, approx: true, includeMap: false);

        if (_lowerGrids.Count == 0)
            return;

        _cache.RebuildUpperGridGroups(bounds);

        foreach (var lowerGrid in _lowerGrids)
        {
            _excludedTiles.Clear();
            if (_motionLinkQuery.TryComp(lowerGrid.Owner, out var lowerLink)
                && !string.IsNullOrEmpty(lowerLink.GroupId)
                && _cache.TryGetUpperGridsForGroup(lowerLink.GroupId, out var linkedUpperGrids))
            {
                foreach (var upperGrid in linkedUpperGrids)
                {
                    _excludedTiles.UnionWith(_cache.GetProjectedMaskTiles(lowerGrid, upperGrid));
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
                if (!def.HasZRoof)
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

    private void ClearBatchedRows()
    {
        foreach (var y in _usedBatchRows)
            _batchedRows[y].Clear();
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
}
