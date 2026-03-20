using System.Numerics;
using Content.Client.Viewport;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._Utopia.ZLevels.Components;
using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Client._Onyx.ZLevels;

public sealed class OnyxZLevelRoofOverlay : Overlay
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDef = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly SharedMapSystem _mapSystem;
    private readonly SharedTransformSystem _xformSystem;
    private readonly EntityLookupSystem _lookup;
    private readonly CESharedZLevelsSystem _zLevels;

    private EntityQuery<GridMotionLinkComponent> _motionLinkQuery;
    private EntityQuery<CEZLevelMapComponent> _zMapQuery;
    private EntityQuery<MapComponent> _mapQuery;

    private List<Entity<MapGridComponent>> _lowerGrids = new();
    private List<Entity<MapGridComponent>> _upperGridsBuffer = new();

    private readonly List<Entity<MapGridComponent>> _linkedUpperGrids = new();
    private readonly HashSet<Vector2i> _excludedTiles = new();

    private readonly Dictionary<(EntityUid, MapId), HashSet<Vector2i>> _exclusionCache = new();
    private TimeSpan _lastCacheRebuild;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(0.5);
    private readonly Dictionary<MapId, EntityUid> _mapIdCache = new();
    private TimeSpan _lastMapIdCacheRebuild;

    private static readonly Color RoofColor = new(0.1f, 0.1f, 0.1f, 1.0f);

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public OnyxZLevelRoofOverlay()
    {
        IoCManager.InjectDependencies(this);

        _mapSystem = _entManager.System<SharedMapSystem>();
        _xformSystem = _entManager.System<SharedTransformSystem>();
        _lookup = _entManager.System<EntityLookupSystem>();
        _zLevels = _entManager.System<CESharedZLevelsSystem>();

        _motionLinkQuery = _entManager.GetEntityQuery<GridMotionLinkComponent>();
        _zMapQuery = _entManager.GetEntityQuery<CEZLevelMapComponent>();
        _mapQuery = _entManager.GetEntityQuery<MapComponent>();

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

        var now = _timing.RealTime;
        var cacheExpired = now - _lastCacheRebuild > CacheLifetime;

        if (upperMapId != null)
        {
            _upperGridsBuffer.Clear();
            _mapManager.FindGridsIntersecting(upperMapId.Value, bounds, ref _upperGridsBuffer, approx: true, includeMap: false);
        }

        foreach (var lowerGrid in _lowerGrids)
        {
            HashSet<Vector2i>? excluded = null;

            if (upperMapId != null)
            {
                var cacheKey = (lowerGrid.Owner, upperMapId.Value);

                if (cacheExpired || !_exclusionCache.TryGetValue(cacheKey, out excluded))
                {
                    _linkedUpperGrids.Clear();
                    _excludedTiles.Clear();
                    FindLinkedUpperGrids(lowerGrid.Owner, _upperGridsBuffer);
                    if (_linkedUpperGrids.Count > 0)
                        BuildExclusionSet(lowerGrid);

                    excluded = new HashSet<Vector2i>(_excludedTiles);
                    _exclusionCache[cacheKey] = excluded;
                }
            }

            if (cacheExpired)
                _lastCacheRebuild = now;

            var gridMatrix = _xformSystem.GetWorldMatrix(lowerGrid.Owner);
            worldHandle.SetTransform(gridMatrix);

            var tileEnumerator = _mapSystem.GetTilesEnumerator(lowerGrid.Owner, lowerGrid, bounds);
            while (tileEnumerator.MoveNext(out var tileRef))
            {
                if (tileRef.Tile.IsEmpty)
                    continue;

                var def = (ContentTileDefinition) _tileDef[tileRef.Tile.TypeId];
                if (!def.HasZRoof)
                    continue;

                if (excluded != null && excluded.Contains(tileRef.GridIndices))
                    continue;

                var local = _lookup.GetLocalBounds(tileRef, lowerGrid.Comp.TileSize);
                worldHandle.DrawRect(local, RoofColor);
            }
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
    }

    private bool TryGetMapUid(MapId mapId, out EntityUid uid)
    {
        var now = _timing.RealTime;
        if (now - _lastMapIdCacheRebuild > CacheLifetime)
        {
            _mapIdCache.Clear();
            _lastMapIdCacheRebuild = now;
        }

        if (_mapIdCache.TryGetValue(mapId, out uid))
            return true;

        var query = _entManager.EntityQueryEnumerator<MapComponent>();
        while (query.MoveNext(out var eid, out var mapComp))
        {
            if (mapComp.MapId == mapId)
            {
                _mapIdCache[mapId] = eid;
                uid = eid;
                return true;
            }
        }

        uid = default;
        return false;
    }

    private void FindLinkedUpperGrids(EntityUid lowerGridUid, List<Entity<MapGridComponent>> upperGrids)
    {
        if (!_motionLinkQuery.TryComp(lowerGridUid, out var lowerLink) || string.IsNullOrEmpty(lowerLink.GroupId))
            return;

        foreach (var upperGrid in upperGrids)
        {
            if (!_motionLinkQuery.TryComp(upperGrid.Owner, out var upperLink))
                continue;

            if (upperLink.GroupId != lowerLink.GroupId)
                continue;

            _linkedUpperGrids.Add(upperGrid);
        }
    }

    private void BuildExclusionSet(Entity<MapGridComponent> lowerGrid)
    {
        foreach (var upperGrid in _linkedUpperGrids)
        {
            var solidTiles = new HashSet<Vector2i>();
            var enumerator = _mapSystem.GetAllTilesEnumerator(upperGrid.Owner, upperGrid.Comp, ignoreEmpty: true);
            while (enumerator.MoveNext(out var upperTileRef))
            {
                var gridPos = upperTileRef.Value.GridIndices;
                solidTiles.Add(gridPos);

                var worldPos = _mapSystem.GridTileToWorldPos(upperGrid.Owner, upperGrid.Comp, gridPos);
                var lowerTilePos = _mapSystem.WorldToTile(lowerGrid.Owner, lowerGrid.Comp, worldPos);
                _excludedTiles.Add(lowerTilePos);
            }

            var interiorHoles = ZLevelFloodFillHelper.FindInteriorHolesFromSolid(solidTiles);
            foreach (var pos in interiorHoles)
            {
                var worldPos = _mapSystem.GridTileToWorldPos(upperGrid.Owner, upperGrid.Comp, pos);
                var lowerTilePos = _mapSystem.WorldToTile(lowerGrid.Owner, lowerGrid.Comp, worldPos);
                _excludedTiles.Add(lowerTilePos);
            }
        }
    }
}
