using System.Numerics;
using Content.Server.Atmos;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._Utopia.ZLevels.Components;
using Content.Shared.Atmos;
using Content.Shared.CCVar;
using Content.Shared.Maps;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Onyx.ZLevels.Atmos;

public sealed class ZLevelGridAtmosSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private EntityQuery<CEZLevelMapComponent> _zMapQuery;
    private EntityQuery<GridAtmosphereComponent> _atmosQuery;
    private EntityQuery<MapGridComponent> _gridQuery;

    private readonly Dictionary<string, List<(int Depth, EntityUid Grid)>> _groupCache = new();
    private bool _groupCacheDirty = true;
    private int _periodicRebuildCounter;

    private readonly Dictionary<(EntityUid Below, EntityUid Above), List<VerticalLink>> _verticalLinks = new();
    private bool _linksDirty = true;
    private readonly HashSet<(EntityUid Grid, Vector2i Tile)> _managedHoleTiles = new();

    private readonly HashSet<(EntityUid Grid, Vector2i Tile)> _dynamicHoleCache = new();

    private record struct VerticalLink(
        EntityUid HoleGrid,
        Vector2i HoleTile,
        EntityUid TargetGrid,
        Vector2i TargetTile);

    public override void Initialize()
    {
        base.Initialize();

        UpdatesBefore.Add(typeof(AtmosphereSystem));

        _zMapQuery = GetEntityQuery<CEZLevelMapComponent>();
        _atmosQuery = GetEntityQuery<GridAtmosphereComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();

        SubscribeLocalEvent<GridMotionLinkComponent, ComponentStartup>(OnLinkChanged);
        SubscribeLocalEvent<GridMotionLinkComponent, ComponentShutdown>(OnLinkChanged);
        SubscribeLocalEvent<GridMotionLinkComponent, EntParentChangedMessage>(OnLinkParentChanged);
        SubscribeLocalEvent<CEZLevelMapComponent, ComponentStartup>(OnZMapChanged);
        SubscribeLocalEvent<CEZLevelMapComponent, ComponentShutdown>(OnZMapChanged);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<MapGridComponent, GridFixtureChangeEvent>(OnGridFixtureChanged);
    }

    public bool IsVerticalHoleTile(EntityUid grid, Vector2i pos)
    {
        if (_managedHoleTiles.Contains((grid, pos)) || _dynamicHoleCache.Contains((grid, pos)))
            return true;

        if (!TryComp<GridMotionLinkComponent>(grid, out var link))
            return false;

        if (string.IsNullOrEmpty(link.GroupId))
            return false;

        if (!_gridQuery.TryComp(grid, out var gridComp))
            return false;

        if (_mapSystem.TryGetTileRef(grid, gridComp, pos, out var selfTile) && !selfTile.Tile.IsEmpty)
        {
            var selfDef = (ContentTileDefinition) _tileDefManager[selfTile.Tile.TypeId];
            if (!selfDef.MapAtmosphere)
                return false;
        }

        var xform = Transform(grid);
        if (xform.MapUid is not { } mapUid)
            return false;

        if (!_zMapQuery.TryComp(mapUid, out var zMap))
            return false;

        var myDepth = zMap.Depth;
        var worldPos = _mapSystem.GridTileToWorldPos(grid, gridComp, pos);

        var query = EntityQueryEnumerator<GridMotionLinkComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var otherUid, out var otherLink, out var otherGrid, out var otherXform))
        {
            if (otherUid == grid)
                continue;

            if (!SameGroup(link, otherLink))
                continue;

            if (otherXform.MapUid is not { } otherMapUid)
                continue;

            if (!_zMapQuery.TryComp(otherMapUid, out var otherZMap))
                continue;

            if (otherZMap.Depth != myDepth - 1)
                continue;

            if (IsSolidDeckTileAtWorld(otherUid, otherGrid, worldPos))
            {
                _dynamicHoleCache.Add((grid, pos));
                return true;
            }
        }
        return false;
    }

    public bool IsEntityOnVerticalHole(EntityUid uid)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } gridUid)
            return false;

        if (!_gridQuery.TryComp(gridUid, out var grid))
            return false;

        var pos = _mapSystem.CoordinatesToTile(gridUid, grid, xform.Coordinates);
        return IsVerticalHoleTile(gridUid, pos);
    }

    private static bool SameGroup(GridMotionLinkComponent a, GridMotionLinkComponent b)
    {
        return !string.IsNullOrEmpty(a.GroupId) && a.GroupId == b.GroupId;
    }

    private void OnLinkChanged<T>(Entity<GridMotionLinkComponent> ent, ref T args)
    {
        _groupCacheDirty = true;
    }

    private void OnLinkParentChanged(Entity<GridMotionLinkComponent> ent, ref EntParentChangedMessage args)
    {
        _groupCacheDirty = true;
    }

    private void OnZMapChanged<T>(Entity<CEZLevelMapComponent> ent, ref T args)
    {
        _groupCacheDirty = true;
    }

    private void OnTileChanged(ref TileChangedEvent ev)
    {
        if (HasComp<GridMotionLinkComponent>(ev.Entity))
            _linksDirty = true;
    }

    private void OnGridFixtureChanged(Entity<MapGridComponent> ent, ref GridFixtureChangeEvent args)
    {
        if (!HasComp<GridMotionLinkComponent>(ent))
            return;

        _linksDirty = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _dynamicHoleCache.Clear();

        if (++_periodicRebuildCounter >= 60)
        {
            _periodicRebuildCounter = 0;
            _groupCacheDirty = true;
        }

        if (_groupCacheDirty)
            RebuildGroupCache();

        if (_linksDirty)
            RebuildVerticalLinks();

        ProcessVerticalAtmos();
    }

    private void RebuildGroupCache()
    {
        _groupCacheDirty = false;
        _linksDirty = true;
        _groupCache.Clear();

        var query = EntityQueryEnumerator<GridMotionLinkComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var link, out _, out var xform))
        {
            if (string.IsNullOrEmpty(link.GroupId))
                continue;

            if (xform.MapUid is not { } mapUid)
                continue;

            if (!_zMapQuery.TryComp(mapUid, out var zMap))
                continue;

            if (!_groupCache.TryGetValue(link.GroupId, out var list))
            {
                list = new List<(int, EntityUid)>();
                _groupCache[link.GroupId] = list;
            }

            list.Add((zMap.Depth, uid));
        }

        foreach (var list in _groupCache.Values)
            list.Sort((a, b) => a.Depth.CompareTo(b.Depth));
    }

    private void RebuildVerticalLinks()
    {
        RestoreAllManagedTiles();

        _linksDirty = false;
        _verticalLinks.Clear();

        foreach (var grids in _groupCache.Values)
        {
            for (var i = 0; i < grids.Count - 1; i++)
            {
                var (depthBelow, belowUid) = grids[i];
                for (var j = i + 1; j < grids.Count; j++)
                {
                    var (depthAbove, aboveUid) = grids[j];
                    var depthDiff = depthAbove - depthBelow;

                    if (depthDiff < 1)
                        continue;

                    if (depthDiff > 1)
                        break;

                    if (!_gridQuery.TryComp(belowUid, out var belowGrid) ||
                        !_gridQuery.TryComp(aboveUid, out var aboveGrid))
                        continue;

                    var links = new List<VerticalLink>();
                    var processed = new HashSet<(EntityUid, Vector2i, EntityUid, Vector2i)>();

                    BuildHoleLinks(links, processed,
                        sourceGridUid: aboveUid, sourceGrid: aboveGrid,
                        targetGridUid: belowUid, targetGrid: belowGrid);

                    InferUpperHolesFromLowerSolids(links, processed,
                        upperGridUid: aboveUid, upperGrid: aboveGrid,
                        lowerGridUid: belowUid, lowerGrid: belowGrid);

                    if (links.Count > 0)
                        _verticalLinks[(belowUid, aboveUid)] = links;
                }
            }
        }
    }

    private void BuildHoleLinks(
        List<VerticalLink> links,
        HashSet<(EntityUid, Vector2i, EntityUid, Vector2i)> processed,
        EntityUid sourceGridUid, MapGridComponent sourceGrid,
        EntityUid targetGridUid, MapGridComponent targetGrid)
    {
        var enumerator = _mapSystem.GetAllTilesEnumerator(sourceGridUid, sourceGrid, ignoreEmpty: false);
        while (enumerator.MoveNext(out var tileRef))
        {
            if (tileRef == null || !IsHoleTile(tileRef.Value.Tile))
                continue;

            var sourcePos = tileRef.Value.GridIndices;
            var worldPos = _mapSystem.GridTileToWorldPos(sourceGridUid, sourceGrid, sourcePos);
            var targetPos = _mapSystem.WorldToTile(targetGridUid, targetGrid, worldPos);

            if (!IsSolidDeckTile(targetGridUid, targetGrid, targetPos))
                continue;

            AddVerticalLink(links, processed, sourceGridUid, sourcePos, targetGridUid, targetPos);
        }
    }

    private void InferUpperHolesFromLowerSolids(
        List<VerticalLink> links,
        HashSet<(EntityUid, Vector2i, EntityUid, Vector2i)> processed,
        EntityUid upperGridUid, MapGridComponent upperGrid,
        EntityUid lowerGridUid, MapGridComponent lowerGrid)
    {
        var enumerator = _mapSystem.GetAllTilesEnumerator(lowerGridUid, lowerGrid, ignoreEmpty: true);
        while (enumerator.MoveNext(out var tileRef))
        {
            if (tileRef == null)
                continue;

            var def = (ContentTileDefinition) _tileDefManager[tileRef.Value.Tile.TypeId];
            if (def.MapAtmosphere)
                continue;

            var lowerPos = tileRef.Value.GridIndices;
            var worldPos = _mapSystem.GridTileToWorldPos(lowerGridUid, lowerGrid, lowerPos);
            var upperPos = _mapSystem.WorldToTile(upperGridUid, upperGrid, worldPos);

            var isUpperHole = true;
            if (_mapSystem.TryGetTileRef(upperGridUid, upperGrid, worldPos, out var upperTileRef))
                isUpperHole = IsHoleTile(upperTileRef.Tile);

            if (!isUpperHole)
                continue;

            AddVerticalLink(links, processed, upperGridUid, upperPos, lowerGridUid, lowerPos);
        }
    }

    private bool IsHoleTile(Tile tile)
    {
        if (tile.IsEmpty)
            return true;

        var def = (ContentTileDefinition) _tileDefManager[tile.TypeId];
        return def.MapAtmosphere;
    }

    private bool IsSolidDeckTile(EntityUid gridUid, MapGridComponent grid, Vector2i tilePos)
    {
        if (!_mapSystem.TryGetTileRef(gridUid, grid, tilePos, out var tileRef))
            return false;

        if (tileRef.Tile.IsEmpty)
            return false;

        var def = (ContentTileDefinition) _tileDefManager[tileRef.Tile.TypeId];
        return !def.MapAtmosphere;
    }

    private bool IsSolidDeckTileAtWorld(EntityUid gridUid, MapGridComponent grid, Vector2 worldPos)
    {
        if (!_mapSystem.TryGetTileRef(gridUid, grid, worldPos, out var tileRef))
            return false;

        if (tileRef.Tile.IsEmpty)
            return false;

        var def = (ContentTileDefinition) _tileDefManager[tileRef.Tile.TypeId];
        return !def.MapAtmosphere;
    }

    private void AddVerticalLink(
        List<VerticalLink> links,
        HashSet<(EntityUid, Vector2i, EntityUid, Vector2i)> processed,
        EntityUid holeGrid, Vector2i holeTile,
        EntityUid targetGrid, Vector2i targetTile)
    {
        if (!processed.Add((holeGrid, holeTile, targetGrid, targetTile)))
            return;

        links.Add(new VerticalLink(holeGrid, holeTile, targetGrid, targetTile));
        _managedHoleTiles.Add((holeGrid, holeTile));

        // Ensure both sides get processed by atmos revalidation even if one side has no grid tile.
        if (_atmosQuery.TryComp(holeGrid, out var holeAtmos))
            holeAtmos.InvalidatedCoords.Add(holeTile);

        if (_atmosQuery.TryComp(targetGrid, out var targetAtmos))
            targetAtmos.InvalidatedCoords.Add(targetTile);
    }

    private void ProcessVerticalAtmos()
    {
        var transferSpeed = MathF.Max(0f, _cfg.GetCVar(CCVars.ZLevelsAtmosTransferSpeed));
        if (transferSpeed <= 0f)
            return;

        foreach (var links in _verticalLinks.Values)
        {
            foreach (var link in links)
            {
                if (!_atmosQuery.TryComp(link.HoleGrid, out var holeAtmos) ||
                    !_atmosQuery.TryComp(link.TargetGrid, out var targetAtmos))
                    continue;

                if (!holeAtmos.Tiles.TryGetValue(link.HoleTile, out var holeTile))
                {
                    holeAtmos.InvalidatedCoords.Add(link.HoleTile);
                    continue;
                }

                if (holeTile.Air == null || holeTile.Air.Immutable)
                {
                    holeAtmos.InvalidatedCoords.Add(link.HoleTile);
                    continue;
                }

                if (!targetAtmos.Tiles.TryGetValue(link.TargetTile, out var targetTile))
                {
                    targetAtmos.InvalidatedCoords.Add(link.TargetTile);
                    continue;
                }

                if (targetTile.Air == null || targetTile.Air.Immutable || targetTile.MapAtmosphere)
                {
                    targetAtmos.InvalidatedCoords.Add(link.TargetTile);
                    continue;
                }

                var deltaP = holeTile.Air.Pressure - targetTile.Air.Pressure;

                if (MathF.Abs(deltaP) < Atmospherics.MinimumMolesDeltaToMove)
                    continue;

                GasMixture srcAir, dstAir;
                TileAtmosphere srcTile, dstTile;
                GridAtmosphereComponent srcAtmos, dstAtmos;

                if (deltaP > 0)
                {
                    srcAir = holeTile.Air;
                    dstAir = targetTile.Air;
                    srcTile = holeTile;
                    dstTile = targetTile;
                    srcAtmos = holeAtmos;
                    dstAtmos = targetAtmos;
                }
                else
                {
                    srcAir = targetTile.Air;
                    dstAir = holeTile.Air;
                    srcTile = targetTile;
                    dstTile = holeTile;
                    srcAtmos = targetAtmos;
                    dstAtmos = holeAtmos;
                    deltaP = -deltaP;
                }

                if (srcAir.TotalMoles <= 0f || srcAir.Temperature <= 0f)
                    continue;

                var transferMoles = deltaP * srcAir.Volume / (srcAir.Temperature * Atmospherics.R);
                transferMoles *= transferSpeed;
                transferMoles = MathF.Min(transferMoles, srcAir.TotalMoles);

                if (transferMoles <= 0f)
                    continue;

                var removed = srcAir.Remove(transferMoles);
                _atmos.Merge(dstAir, removed);

                ActivateTile(srcAtmos, srcTile);
                ActivateTile(dstAtmos, dstTile);
            }
        }
    }

    private static void ActivateTile(GridAtmosphereComponent atmos, TileAtmosphere tile)
    {
        if (tile.Air == null || tile.Excited)
            return;

        tile.Excited = true;
        atmos.ActiveTiles.Add(tile);
    }

    private void RestoreAllManagedTiles()
    {
        foreach (var (gridUid, pos) in _managedHoleTiles)
        {
            if (!_atmosQuery.TryComp(gridUid, out var atmos))
                continue;

            atmos.InvalidatedCoords.Add(pos);
        }

        _managedHoleTiles.Clear();
    }
}
