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

    private float _cachedOpacity;
    private Color _cachedShadowBaseColor = Color.Black;
    private Color _cachedFillColor = Color.Black;

    private readonly HashSet<Vector2i> _projectedHoleTiles = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

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

        var holes = ZLevelFloodFillHelper.FindInteriorHoles(_mapSystem, upperGrid);
        _upperInteriorHoleCache[upperGrid.Owner] = holes;
        return holes;
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
