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
    private EntityQuery<GridMotionLinkComponent> _motionLinkQuery;

    private readonly Dictionary<string, List<(int Depth, EntityUid Grid)>> _groupCache = new();
    private bool _groupCacheDirty = true;
    private int _periodicRebuildCounter;

    private readonly Dictionary<(EntityUid Below, EntityUid Above), List<VerticalLink>> _verticalLinks = new();
    private bool _linksDirty = true;
    private readonly HashSet<(EntityUid Grid, Vector2i Tile)> _managedHoleTiles = new();
    private readonly Dictionary<EntityUid, HashSet<Vector2i>> _holeTilesPerGrid = new();
    private readonly HashSet<EntityUid> _linkedGrids = new();
    private readonly List<(EntityUid Grid, Vector2i Tile)> _pendingTileUpdates = new();

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
        _motionLinkQuery = GetEntityQuery<GridMotionLinkComponent>();

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
        return _managedHoleTiles.Contains((grid, pos));
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
        if (!_linkedGrids.Contains(ev.Entity))
            return;

        foreach (var change in ev.Changes)
        {
            _pendingTileUpdates.Add((ev.Entity, change.GridIndices));
        }
    }

    private void OnGridFixtureChanged(Entity<MapGridComponent> ent, ref GridFixtureChangeEvent args)
    {
        if (!_motionLinkQuery.HasComp(ent))
            return;

        _linksDirty = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (++_periodicRebuildCounter >= 60)
        {
            _periodicRebuildCounter = 0;
            _groupCacheDirty = true;
        }

        if (_groupCacheDirty)
            RebuildGroupCache();

        if (_linksDirty)
        {
            RebuildVerticalLinks();
        }
        else if (_pendingTileUpdates.Count > 0)
        {
            ProcessIncrementalTileUpdates();
        }

        ProcessVerticalAtmos();
    }

    private void ProcessIncrementalTileUpdates()
    {
        foreach (var (gridUid, tilePos) in _pendingTileUpdates)
        {
            UpdateSingleTileHoleStatus(gridUid, tilePos);
        }

        _pendingTileUpdates.Clear();
    }

    private void UpdateSingleTileHoleStatus(EntityUid gridUid, Vector2i tilePos)
    {
        if (!_motionLinkQuery.TryComp(gridUid, out var link) || string.IsNullOrEmpty(link.GroupId))
            return;

        if (!_gridQuery.TryComp(gridUid, out var gridComp))
            return;

        var xform = Transform(gridUid);
        if (xform.MapUid is not { } mapUid || !_zMapQuery.TryComp(mapUid, out var zMap))
            return;

        var myDepth = zMap.Depth;

        if (!_groupCache.TryGetValue(link.GroupId, out var groupGrids))
            return;

        var wasHole = _managedHoleTiles.Contains((gridUid, tilePos));
        var worldPos = _mapSystem.GridTileToWorldPos(gridUid, gridComp, tilePos);

        var isCurrentlyHole = IsHoleTileOnGrid(gridUid, gridComp, tilePos);

        if (isCurrentlyHole)
        {
            var hasSolidBelow = false;
            foreach (var (depth, otherUid) in groupGrids)
            {
                if (depth != myDepth - 1 || otherUid == gridUid)
                    continue;

                if (!_gridQuery.TryComp(otherUid, out var otherGrid))
                    continue;

                if (IsSolidDeckTileAtWorld(otherUid, otherGrid, worldPos))
                {
                    hasSolidBelow = true;
                    break;
                }
            }

            if (hasSolidBelow && !wasHole)
            {
                AddHoleTile(gridUid, tilePos);
                RebuildLinksForTile(gridUid, tilePos, worldPos, myDepth, groupGrids);
            }
            else if (!hasSolidBelow && wasHole)
            {
                RemoveHoleTile(gridUid, tilePos);
                RemoveLinksForTile(gridUid, tilePos);
            }
        }
        else if (wasHole)
        {
            RemoveHoleTile(gridUid, tilePos);
            RemoveLinksForTile(gridUid, tilePos);
        }

        foreach (var (depth, otherUid) in groupGrids)
        {
            if (depth != myDepth + 1 || otherUid == gridUid)
                continue;

            if (!_gridQuery.TryComp(otherUid, out var otherGrid))
                continue;

            var otherPos = _mapSystem.WorldToTile(otherUid, otherGrid, worldPos);
            var otherIsHole = IsHoleTileOnGrid(otherUid, otherGrid, otherPos);
            var otherWasManaged = _managedHoleTiles.Contains((otherUid, otherPos));
            var currentIsSolid = IsSolidDeckTile(gridUid, gridComp, tilePos);

            if (otherIsHole && currentIsSolid && !otherWasManaged)
            {
                AddHoleTile(otherUid, otherPos);
                RebuildLinksForTile(otherUid, otherPos, worldPos, myDepth + 1, groupGrids);
            }
            else if ((!otherIsHole || !currentIsSolid) && otherWasManaged)
            {
                RemoveHoleTile(otherUid, otherPos);
                RemoveLinksForTile(otherUid, otherPos);
            }
        }
    }

    private bool IsHoleTileOnGrid(EntityUid gridUid, MapGridComponent grid, Vector2i tilePos)
    {
        if (!_mapSystem.TryGetTileRef(gridUid, grid, tilePos, out var tileRef))
            return true;

        return IsHoleTile(tileRef.Tile);
    }

    private void AddHoleTile(EntityUid grid, Vector2i tile)
    {
        _managedHoleTiles.Add((grid, tile));

        if (!_holeTilesPerGrid.TryGetValue(grid, out var set))
        {
            set = new HashSet<Vector2i>();
            _holeTilesPerGrid[grid] = set;
        }
        set.Add(tile);

        if (_atmosQuery.TryComp(grid, out var atmos))
            atmos.InvalidatedCoords.Add(tile);
    }

    private void RemoveHoleTile(EntityUid grid, Vector2i tile)
    {
        _managedHoleTiles.Remove((grid, tile));

        if (_holeTilesPerGrid.TryGetValue(grid, out var set))
            set.Remove(tile);

        if (_atmosQuery.TryComp(grid, out var atmos))
            atmos.InvalidatedCoords.Add(tile);
    }

    private void RebuildLinksForTile(EntityUid holeGrid, Vector2i holeTile, Vector2 worldPos, int holeDepth,
        List<(int Depth, EntityUid Grid)> groupGrids)
    {
        foreach (var (depth, targetUid) in groupGrids)
        {
            if (depth != holeDepth - 1 || targetUid == holeGrid)
                continue;

            if (!_gridQuery.TryComp(targetUid, out var targetGrid))
                continue;

            var targetPos = _mapSystem.WorldToTile(targetUid, targetGrid, worldPos);

            if (!IsSolidDeckTile(targetUid, targetGrid, targetPos))
                continue;

            var key = (targetUid, holeGrid);
            if (!_verticalLinks.TryGetValue(key, out var links))
            {
                links = new List<VerticalLink>();
                _verticalLinks[key] = links;
            }

            var newLink = new VerticalLink(holeGrid, holeTile, targetUid, targetPos);
            if (!links.Contains(newLink))
                links.Add(newLink);

            if (_atmosQuery.TryComp(targetUid, out var targetAtmos))
                targetAtmos.InvalidatedCoords.Add(targetPos);

            break;
        }
    }

    private void RemoveLinksForTile(EntityUid holeGrid, Vector2i holeTile)
    {
        foreach (var (key, links) in _verticalLinks)
        {
            for (var i = links.Count - 1; i >= 0; i--)
            {
                var link = links[i];
                if (link.HoleGrid == holeGrid && link.HoleTile == holeTile)
                {
                    if (_atmosQuery.TryComp(link.TargetGrid, out var targetAtmos))
                        targetAtmos.InvalidatedCoords.Add(link.TargetTile);

                    links.RemoveAt(i);
                }
            }
        }
    }

    private void RebuildGroupCache()
    {
        _groupCacheDirty = false;
        _linksDirty = true;
        _groupCache.Clear();
        _linkedGrids.Clear();

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
            _linkedGrids.Add(uid);
        }

        foreach (var list in _groupCache.Values)
            list.Sort((a, b) => a.Depth.CompareTo(b.Depth));
    }

    private void RebuildVerticalLinks()
    {
        RestoreAllManagedTiles();

        _linksDirty = false;
        _verticalLinks.Clear();
        _pendingTileUpdates.Clear();

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

        if (!_holeTilesPerGrid.TryGetValue(holeGrid, out var set))
        {
            set = new HashSet<Vector2i>();
            _holeTilesPerGrid[holeGrid] = set;
        }
        set.Add(holeTile);

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
        _holeTilesPerGrid.Clear();
    }
}
