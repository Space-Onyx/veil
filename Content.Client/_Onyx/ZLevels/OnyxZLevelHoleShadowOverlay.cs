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

    private readonly CESharedZLevelsSystem _zLevels;
    private readonly SharedMapSystem _mapSystem;
    private readonly SharedTransformSystem _xform;
    private readonly EntityLookupSystem _lookup;

    private EntityQuery<CEZLevelMapComponent> _zMapQuery;
    private EntityQuery<MapComponent> _mapQuery;
    private EntityQuery<GridMotionLinkComponent> _motionLinkQuery;

    private List<Entity<MapGridComponent>> _lowerGrids = new();
    private List<Entity<MapGridComponent>> _upperGrids = new();
    private readonly Dictionary<string, List<Entity<MapGridComponent>>> _upperGridsByGroup = new();
    private readonly List<string> _usedUpperGroupKeys = new();
    private readonly Dictionary<MapId, EntityUid> _mapIdCache = new();
    private readonly Dictionary<EntityUid, HashSet<Vector2i>> _upperInteriorHoleCache = new();
    private readonly Dictionary<(EntityUid LowerGrid, EntityUid UpperGrid), HashSet<Vector2i>> _pairProjectionCache = new();
    private TimeSpan _lastMapCacheRebuild;
    private TimeSpan _lastHoleCacheRebuild;
    private static readonly TimeSpan MapCacheLifetime = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan HoleCacheLifetime = TimeSpan.FromSeconds(0.25);

    private readonly HashSet<Vector2i> _solidTiles = new();
    private readonly HashSet<Vector2i> _outerEmpty = new();
    private readonly Queue<Vector2i> _floodQueue = new();
    private readonly HashSet<Vector2i> _projectedHoleTiles = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    public OnyxZLevelHoleShadowOverlay()
    {
        IoCManager.InjectDependencies(this);

        _zLevels = _entManager.System<CESharedZLevelsSystem>();
        _mapSystem = _entManager.System<SharedMapSystem>();
        _xform = _entManager.System<SharedTransformSystem>();
        _lookup = _entManager.System<EntityLookupSystem>();

        _zMapQuery = _entManager.GetEntityQuery<CEZLevelMapComponent>();
        _mapQuery = _entManager.GetEntityQuery<MapComponent>();
        _motionLinkQuery = _entManager.GetEntityQuery<GridMotionLinkComponent>();

        ZIndex = 100;
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

        _upperGrids.Clear();
        _mapManager.FindGridsIntersecting(upperMapId, args.WorldBounds, ref _upperGrids, approx: true, includeMap: false);

        if (_upperGrids.Count == 0)
            return;

        var now = _timing.RealTime;
        if (now - _lastHoleCacheRebuild > HoleCacheLifetime)
        {
            _upperInteriorHoleCache.Clear();
            _pairProjectionCache.Clear();
            _lastHoleCacheRebuild = now;
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
        var transparency = Math.Clamp(_cfg.GetCVar(CCVars.ZLevelHoleShadowOpacity), 0f, 1f);
        var alpha = 1f - transparency;
        var shadowBaseColor = Color.FromHex(_cfg.GetCVar(CCVars.ZLevelHoleShadowColor), Color.Black);
        var holeShadowFillColor = shadowBaseColor.WithAlpha(alpha);

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

            var tileEnumerator = _mapSystem.GetTilesEnumerator(lowerGrid.Owner, lowerGrid, args.WorldBounds);
            while (tileEnumerator.MoveNext(out var tileRef))
            {
                if (tileRef.Tile.IsEmpty)
                    continue;

                if (!_projectedHoleTiles.Contains(tileRef.GridIndices))
                    continue;

                var local = _lookup.GetLocalBounds(tileRef, lowerGrid.Comp.TileSize);
                worldHandle.DrawRect(local, holeShadowFillColor);
            }
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
    }

    private HashSet<Vector2i> GetProjectedHoleTiles(Entity<MapGridComponent> lowerGrid, Entity<MapGridComponent> upperGrid)
    {
        var key = (lowerGrid.Owner, upperGrid.Owner);
        if (_pairProjectionCache.TryGetValue(key, out var cachedProjected))
            return cachedProjected;

        var upperHoles = GetInteriorHoleTiles(upperGrid);
        var projected = new HashSet<Vector2i>();

        if (upperHoles.Count == 0)
        {
            _pairProjectionCache[key] = projected;
            return projected;
        }

        foreach (var pos in upperHoles)
        {
            var worldPos = _mapSystem.GridTileToWorldPos(upperGrid.Owner, upperGrid.Comp, pos);
            var lowerTilePos = _mapSystem.WorldToTile(lowerGrid.Owner, lowerGrid.Comp, worldPos);
            projected.Add(lowerTilePos);
        }

        _pairProjectionCache[key] = projected;
        return projected;
    }

    private HashSet<Vector2i> GetInteriorHoleTiles(Entity<MapGridComponent> upperGrid)
    {
        if (_upperInteriorHoleCache.TryGetValue(upperGrid.Owner, out var cachedHoles))
            return cachedHoles;

        var holes = new HashSet<Vector2i>();
        _solidTiles.Clear();
        _outerEmpty.Clear();
        _floodQueue.Clear();

        var upperEnum = _mapSystem.GetAllTilesEnumerator(upperGrid.Owner, upperGrid.Comp, ignoreEmpty: true);
        while (upperEnum.MoveNext(out var upperTile))
        {
            _solidTiles.Add(upperTile.Value.GridIndices);
        }

        if (_solidTiles.Count == 0)
        {
            _upperInteriorHoleCache[upperGrid.Owner] = holes;
            return holes;
        }

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;

        foreach (var pos in _solidTiles)
        {
            if (pos.X < minX) minX = pos.X;
            if (pos.Y < minY) minY = pos.Y;
            if (pos.X > maxX) maxX = pos.X;
            if (pos.Y > maxY) maxY = pos.Y;
        }

        minX--;
        minY--;
        maxX++;
        maxY++;

        for (var x = minX; x <= maxX; x++)
        {
            TryEnqueueFlood(new Vector2i(x, minY));
            TryEnqueueFlood(new Vector2i(x, maxY));
        }

        for (var y = minY + 1; y < maxY; y++)
        {
            TryEnqueueFlood(new Vector2i(minX, y));
            TryEnqueueFlood(new Vector2i(maxX, y));
        }

        while (_floodQueue.Count > 0)
        {
            var current = _floodQueue.Dequeue();

            if (current.X < minX || current.X > maxX || current.Y < minY || current.Y > maxY)
                continue;

            TryEnqueueFlood(new Vector2i(current.X + 1, current.Y));
            TryEnqueueFlood(new Vector2i(current.X - 1, current.Y));
            TryEnqueueFlood(new Vector2i(current.X, current.Y + 1));
            TryEnqueueFlood(new Vector2i(current.X, current.Y - 1));
        }

        for (var x = minX + 1; x < maxX; x++)
        {
            for (var y = minY + 1; y < maxY; y++)
            {
                var pos = new Vector2i(x, y);
                if (_solidTiles.Contains(pos))
                    continue;
                if (_outerEmpty.Contains(pos))
                    continue;

                holes.Add(pos);
            }
        }

        _upperInteriorHoleCache[upperGrid.Owner] = holes;
        return holes;
    }

    private void TryEnqueueFlood(Vector2i pos)
    {
        if (_solidTiles.Contains(pos))
            return;

        if (!_outerEmpty.Add(pos))
            return;

        _floodQueue.Enqueue(pos);
    }

    private bool TryGetMapUid(MapId mapId, out EntityUid uid)
    {
        var now = _timing.RealTime;
        if (now - _lastMapCacheRebuild > MapCacheLifetime)
        {
            _mapIdCache.Clear();
            _lastMapCacheRebuild = now;
        }

        if (_mapIdCache.TryGetValue(mapId, out uid))
            return true;

        var query = _entManager.EntityQueryEnumerator<MapComponent>();
        while (query.MoveNext(out var mapUid, out var mapComp))
        {
            if (mapComp.MapId != mapId)
                continue;

            _mapIdCache[mapId] = mapUid;
            uid = mapUid;
            return true;
        }

        uid = default;
        return false;
    }
}
