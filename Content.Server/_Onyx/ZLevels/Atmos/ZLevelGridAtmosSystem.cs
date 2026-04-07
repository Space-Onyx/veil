using System.Numerics;
using Content.Server.Atmos;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Onyx.ZLevels.Core.Components;
using Content.Shared._Onyx.ZLevels;
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
    private int _periodicGroupCheckCounter;

    private readonly Dictionary<(EntityUid Below, EntityUid Above), List<VerticalLink>> _verticalLinks = new();
    private readonly Dictionary<EntityUid, List<(EntityUid Below, EntityUid Above)>> _verticalLinkKeysByAboveGrid = new();
    private bool _linksDirty = true;
    private readonly HashSet<(EntityUid Grid, Vector2i Tile)> _managedHoleTiles = new();
    private readonly Dictionary<EntityUid, HashSet<Vector2i>> _holeTilesPerGrid = new();
    private readonly HashSet<EntityUid> _linkedGrids = new();
    private readonly List<(EntityUid Grid, Vector2i Tile)> _pendingTileUpdates = new();
    private readonly HashSet<(EntityUid Grid, Vector2i Tile)> _pendingTileUpdateSet = new();
    private readonly Dictionary<EntityUid, HashSet<Vector2i>> _interiorHolesCache = new();
    private readonly List<(EntityUid Below, EntityUid Above)> _verticalLinkKeyBuffer = new();
    private readonly List<string> _staleGroupIds = new();
    private bool _hasZNetwork;
    private float _cachedAtmosTransferSpeed;

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

        SubscribeLocalEvent<GridMotionLinkComponent, EntParentChangedMessage>(OnLinkParentChanged);
        SubscribeLocalEvent<CEZLevelMapComponent, ComponentStartup>(OnZMapChanged);
        SubscribeLocalEvent<CEZLevelMapComponent, ComponentShutdown>(OnZMapChanged);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<MapGridComponent, GridFixtureChangeEvent>(OnGridFixtureChanged);

        _cfg.OnValueChanged(CCVars.ZLevelsAtmosTransferSpeed, value =>
        {
            _cachedAtmosTransferSpeed = MathF.Max(0f, value);
        }, true);
    }


    public bool IsVerticalHoleTile(EntityUid grid, Vector2i pos)
    {
        return _managedHoleTiles.Contains((grid, pos));
    }

    public void ForceRebuildLinks()
    {
        _groupCacheDirty = true;
        RebuildGroupCache();
        _linksDirty = true;
        RebuildVerticalLinks();
    }

    public void EnsureInteriorHolesRegistered(EntityUid gridUid, MapGridComponent grid)
    {
        if (_holeTilesPerGrid.TryGetValue(gridUid, out var existing) && existing.Count > 0)
            return;

        var interiorHoles = GetInteriorHoles(gridUid, grid);
        foreach (var hole in interiorHoles)
        {
            _managedHoleTiles.Add((gridUid, hole));

            if (!_holeTilesPerGrid.TryGetValue(gridUid, out var set))
            {
                set = new HashSet<Vector2i>();
                _holeTilesPerGrid[gridUid] = set;
            }
            set.Add(hole);
        }
    }

    public void CopyVerticalHoleTiles(EntityUid grid, List<Vector2i> buffer)
    {
        if (!_holeTilesPerGrid.TryGetValue(grid, out var tiles) || tiles.Count == 0)
            return;

        buffer.AddRange(tiles);
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

        _interiorHolesCache.Remove(ev.Entity);

        foreach (var change in ev.Changes)
        {
            var update = (ev.Entity, change.GridIndices);
            if (_pendingTileUpdateSet.Add(update))
                _pendingTileUpdates.Add(update);
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

        if (!_hasZNetwork)
        {
            _hasZNetwork = _groupCache.Count > 0 || _groupCacheDirty;
            if (!_hasZNetwork)
                return;
        }

        if (++_periodicGroupCheckCounter >= 120)
        {
            _periodicGroupCheckCounter = 0;
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
        _pendingTileUpdateSet.Clear();
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

        var interiorHoles = GetInteriorHoles(gridUid, gridComp);
        var isCurrentlyHole = interiorHoles.Contains(tilePos);

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
            var otherInteriorHoles = GetInteriorHoles(otherUid, otherGrid);
            var otherIsHole = otherInteriorHoles.Contains(otherPos);
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
                RegisterVerticalLinkKey(key);
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
        if (!_verticalLinkKeysByAboveGrid.TryGetValue(holeGrid, out var candidateKeys))
            return;

        _verticalLinkKeyBuffer.Clear();
        _verticalLinkKeyBuffer.AddRange(candidateKeys);

        foreach (var key in _verticalLinkKeyBuffer)
        {
            if (!_verticalLinks.TryGetValue(key, out var links))
            {
                UnregisterVerticalLinkKey(key);
                continue;
            }

            for (var i = links.Count - 1; i >= 0; i--)
            {
                var link = links[i];
                if (link.HoleGrid != holeGrid || link.HoleTile != holeTile)
                    continue;

                if (_atmosQuery.TryComp(link.TargetGrid, out var targetAtmos))
                    targetAtmos.InvalidatedCoords.Add(link.TargetTile);

                links.RemoveAt(i);
            }

            if (links.Count == 0)
            {
                _verticalLinks.Remove(key);
                UnregisterVerticalLinkKey(key);
            }
        }
    }

    private void RebuildGroupCache()
    {
        _groupCacheDirty = false;

        foreach (var list in _groupCache.Values)
            list.Clear();

        var newLinkedGrids = new HashSet<EntityUid>();

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
            newLinkedGrids.Add(uid);
        }

        _staleGroupIds.Clear();
        foreach (var (groupId, list) in _groupCache)
        {
            if (list.Count == 0)
                _staleGroupIds.Add(groupId);
        }
        foreach (var stale in _staleGroupIds)
            _groupCache.Remove(stale);

        foreach (var list in _groupCache.Values)
            list.Sort((a, b) => a.Depth.CompareTo(b.Depth));

        if (!newLinkedGrids.SetEquals(_linkedGrids))
            _linksDirty = true;

        _linkedGrids.Clear();
        _linkedGrids.UnionWith(newLinkedGrids);
    }

    private void RebuildVerticalLinks()
    {
        RestoreAllManagedTiles();

        _linksDirty = false;
        _verticalLinks.Clear();
        _verticalLinkKeysByAboveGrid.Clear();
        _pendingTileUpdates.Clear();
        _pendingTileUpdateSet.Clear();
        _interiorHolesCache.Clear();

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
                    {
                        var key = (belowUid, aboveUid);
                        _verticalLinks[key] = links;
                        RegisterVerticalLinkKey(key);
                    }
                }
            }
        }
    }

    private void RegisterVerticalLinkKey((EntityUid Below, EntityUid Above) key)
    {
        if (!_verticalLinkKeysByAboveGrid.TryGetValue(key.Above, out var keys))
        {
            keys = new List<(EntityUid Below, EntityUid Above)>();
            _verticalLinkKeysByAboveGrid[key.Above] = keys;
        }

        if (!keys.Contains(key))
            keys.Add(key);
    }

    private void UnregisterVerticalLinkKey((EntityUid Below, EntityUid Above) key)
    {
        if (!_verticalLinkKeysByAboveGrid.TryGetValue(key.Above, out var keys))
            return;

        keys.Remove(key);
        if (keys.Count == 0)
            _verticalLinkKeysByAboveGrid.Remove(key.Above);
    }

    private void BuildHoleLinks(
        List<VerticalLink> links,
        HashSet<(EntityUid, Vector2i, EntityUid, Vector2i)> processed,
        EntityUid sourceGridUid, MapGridComponent sourceGrid,
        EntityUid targetGridUid, MapGridComponent targetGrid)
    {
        var interiorHoles = GetInteriorHoles(sourceGridUid, sourceGrid);

        foreach (var sourcePos in interiorHoles)
        {
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
        var upperHoles = GetInteriorHoles(upperGridUid, upperGrid);

        if (upperHoles.Count == 0)
            return;

        foreach (var upperPos in upperHoles)
        {
            var worldPos = _mapSystem.GridTileToWorldPos(upperGridUid, upperGrid, upperPos);
            var lowerPos = _mapSystem.WorldToTile(lowerGridUid, lowerGrid, worldPos);

            if (!IsSolidDeckTile(lowerGridUid, lowerGrid, lowerPos))
                continue;

            AddVerticalLink(links, processed, upperGridUid, upperPos, lowerGridUid, lowerPos);
        }
    }

    private HashSet<Vector2i> GetInteriorHoles(EntityUid gridUid, MapGridComponent grid)
    {
        if (_interiorHolesCache.TryGetValue(gridUid, out var cached))
            return cached;

        var holes = ZLevelFloodFillHelper.FindInteriorHoles(_mapSystem, (gridUid, grid), _tileDefManager);
        _interiorHolesCache[gridUid] = holes;
        return holes;
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
        if (_verticalLinks.Count == 0)
            return;

        var transferSpeed = _cachedAtmosTransferSpeed;
        if (transferSpeed <= 0f)
            return;

        var transferFactor = MathF.Min(transferSpeed * 0.4f, 1f);

        foreach (var ((belowUid, aboveUid), links) in _verticalLinks)
        {
            if (!_atmosQuery.TryComp(aboveUid, out var holeAtmos) ||
                !_atmosQuery.TryComp(belowUid, out var targetAtmos))
                continue;

            foreach (var link in links)
            {
                TransferGasBetweenTiles(holeAtmos, link.HoleTile, targetAtmos, link.TargetTile, transferFactor);
            }
        }
    }

    private void TransferGasBetweenTiles(
        GridAtmosphereComponent holeAtmos, Vector2i holeTilePos,
        GridAtmosphereComponent targetAtmos, Vector2i targetTilePos,
        float transferFactor)
    {
        if (!holeAtmos.Tiles.TryGetValue(holeTilePos, out var holeTile))
            return;

        if (holeTile.Air == null || holeTile.Air.Immutable)
            return;

        if (!targetAtmos.Tiles.TryGetValue(targetTilePos, out var targetTile))
            return;

        if (targetTile.Air == null || targetTile.Air.Immutable || targetTile.MapAtmosphere)
            return;

        var pressureDelta = holeTile.Air.Pressure - targetTile.Air.Pressure;
        if (MathF.Abs(pressureDelta) <= 0.001f)
            return;

        GasMixture highPressureAir;
        GasMixture lowPressureAir;
        TileAtmosphere highPressureTile;
        TileAtmosphere lowPressureTile;
        GridAtmosphereComponent highPressureAtmos;
        GridAtmosphereComponent lowPressureAtmos;

        if (pressureDelta > 0)
        {
            highPressureAir = holeTile.Air;
            lowPressureAir = targetTile.Air;
            highPressureTile = holeTile;
            lowPressureTile = targetTile;
            highPressureAtmos = holeAtmos;
            lowPressureAtmos = targetAtmos;
        }
        else
        {
            highPressureAir = targetTile.Air;
            lowPressureAir = holeTile.Air;
            highPressureTile = targetTile;
            lowPressureTile = holeTile;
            highPressureAtmos = targetAtmos;
            lowPressureAtmos = holeAtmos;
        }

        if (highPressureAir.TotalMoles <= 0f ||
            highPressureAir.Temperature <= 0f ||
            lowPressureAir.Temperature <= 0f)
            return;

        var transferMoles = ComputeVerticalEqualizationTransferMoles(highPressureAir, lowPressureAir, transferFactor);
        if (!float.IsFinite(transferMoles))
            return;

        if (transferMoles < Atmospherics.MinimumMolesDeltaToMove)
            return;

        transferMoles = MathF.Min(transferMoles, highPressureAir.TotalMoles);
        if (transferMoles <= 0f)
            return;

        var removed = highPressureAir.Remove(transferMoles);
        _atmos.Merge(lowPressureAir, removed);

        ActivateTile(highPressureAtmos, highPressureTile);
        ActivateTile(lowPressureAtmos, lowPressureTile);
    }

    private float ComputeVerticalEqualizationTransferMoles(GasMixture highPressureAir, GasMixture lowPressureAir, float transferFactor)
    {
        if (highPressureAir.TotalMoles <= 0f || transferFactor <= 0f)
            return 0f;

        var fraction = _atmos.FractionToEqualizePressure(highPressureAir, lowPressureAir);

        // Fallback for rare unstable states (e.g. degenerate heat capacity ratios).
        if (!float.IsFinite(fraction))
        {
            if (highPressureAir.Temperature <= 0f)
                return 0f;

            var pressureDelta = MathF.Max(0f, highPressureAir.Pressure - lowPressureAir.Pressure);
            var idealTransfer = pressureDelta * lowPressureAir.Volume / (highPressureAir.Temperature * Atmospherics.R);
            fraction = idealTransfer / highPressureAir.TotalMoles;
        }

        fraction = Math.Clamp(fraction, 0f, 1f);
        if (fraction <= 0f)
            return 0f;

        return highPressureAir.TotalMoles * fraction * transferFactor;
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
