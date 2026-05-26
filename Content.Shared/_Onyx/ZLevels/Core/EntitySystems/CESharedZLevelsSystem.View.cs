/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._Onyx.ZLevels.Core.Components;
using Content.Shared._Utopia.ZLevels.Components;
using Content.Shared.Actions;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using System.Collections.Generic;
using System.Numerics;

namespace Content.Shared._Onyx.ZLevels.Core.EntitySystems;

public abstract partial class CESharedZLevelsSystem
{
    [Dependency] protected readonly ITileDefinitionManager TilDefMan = default!;
    private static readonly TimeSpan CurrentRoofCacheCleanupInterval = TimeSpan.FromSeconds(5f);
    private const int CurrentRoofCacheSoftLimit = 8192;
    private TimeSpan _nextCurrentRoofCacheCleanup = TimeSpan.Zero;
    private void InitView()
    {
        SubscribeLocalEvent<CEZLevelViewerComponent, MoveEvent>(OnViewerMove);
        SubscribeLocalEvent<CEZLevelViewerComponent, CEToggleZLevelLookUpAction>(OnToggleLookUp);
        SubscribeLocalEvent<TileChangedEvent>(OnViewTileChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateView();
        UpdateMovement(frameTime);
    }

    private void UpdateView()
    {
        if (_currentRoofCacheDirty)
        {
            _currentRoofCache.Clear();
            _currentRoofCacheDirty = false;
            _nextCurrentRoofCacheCleanup = _timing.CurTime + CurrentRoofCacheCleanupInterval;
            return;
        }

        if (_currentRoofCache.Count == 0)
            return;

        if (_currentRoofCache.Count > CurrentRoofCacheSoftLimit ||
            _timing.CurTime >= _nextCurrentRoofCacheCleanup)
        {
            _currentRoofCache.Clear();
            _nextCurrentRoofCacheCleanup = _timing.CurTime + CurrentRoofCacheCleanupInterval;
        }
    }

    private void OnViewTileChanged(ref TileChangedEvent ev)
    {
        _currentRoofCacheDirty = true;
        var gridXform = Transform(ev.Entity);
        if (gridXform.MapUid is { } mapUid)
            InvalidateGroundCacheForMap(mapUid, includeAdjacentMaps: true);

        foreach (var change in ev.Changes)
        {
            WakeEntitiesOnTile(ev.Entity, change.GridIndices);
        }
    }

    protected virtual void OnViewerMove(Entity<CEZLevelViewerComponent> ent, ref MoveEvent args)
    {
        if (!ent.Comp.LookUp)
            return;

        if (!HasOpaqueAbove(ent))
            return;

        ent.Comp.LookUp = false;
        DirtyField(ent, ent.Comp, nameof(CEZLevelViewerComponent.LookUp));
    }

    private void OnToggleLookUp(Entity<CEZLevelViewerComponent> ent, ref CEToggleZLevelLookUpAction args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (HasOpaqueAbove(ent))
        {
            _popup.PopupClient(Loc.GetString("ce-zlevel-look-up-fail"), ent, ent);
            return;
        }

        ent.Comp.LookUp = !ent.Comp.LookUp;
        DirtyField(ent, ent.Comp, nameof(CEZLevelViewerComponent.LookUp));
    }

    private readonly Dictionary<(EntityUid LowerGrid, Vector2i TilePos), bool> _currentRoofCache = new();
    private List<Entity<MapGridComponent>> _opaqueProbeGrids = new();
    private bool _currentRoofCacheDirty;
    public bool HasOpaqueAbove(EntityUid ent, Entity<CEZLevelMapComponent?>? currentMapUid = null)
    {
        var xform = Transform(ent);
        currentMapUid ??= xform.MapUid;

        if (currentMapUid is null)
            return false;

        var worldPosition = _transform.GetWorldPosition(ent);
        if (!TryGetCurrentTile(xform, worldPosition, out var lowerGrid))
            return false;

        if (!TryMapUp(currentMapUid.Value, out var mapAboveUid))
            return HasCurrentTileZRoof(lowerGrid);

        return HasOpaqueLinkedTileAbove(mapAboveUid.Value, lowerGrid.Grid, worldPosition, out var foundLinkedGrid)
            || !foundLinkedGrid && HasCurrentTileZRoof(lowerGrid);
    }

    private bool TryGetCurrentTile(
        TransformComponent xform,
        Vector2 worldPosition,
        out (Entity<MapGridComponent> Grid, TileRef Tile) currentTile)
    {
        if (xform.GridUid is { } gridUid && _gridQuery.HasComp(gridUid))
        {
            Entity<MapGridComponent> grid = (gridUid, _gridQuery.GetComponent(gridUid));
            if (_map.TryGetTileRef(grid.Owner, grid.Comp, worldPosition, out var tileRef) &&
                !tileRef.Tile.IsEmpty)
            {
                currentTile = (grid, tileRef);
                return true;
            }
        }

        if (xform.MapUid is not { } mapUid ||
            !_mapQuery.TryComp(mapUid, out var mapComp))
        {
            currentTile = default;
            return false;
        }

        foreach (var grid in GetCachedGrids(mapComp.MapId))
        {
            if (!_map.TryGetTileRef(grid.Owner, grid.Comp, worldPosition, out var tileRef))
                continue;

            if (tileRef.Tile.IsEmpty)
                continue;

            currentTile = (grid, tileRef);
            return true;
        }

        currentTile = default;
        return false;
    }

    private bool HasCurrentTileZRoof((Entity<MapGridComponent> Grid, TileRef Tile) currentTile)
    {
        var key = (currentTile.Grid.Owner, currentTile.Tile.GridIndices);
        if (!_currentRoofCacheDirty && _currentRoofCache.TryGetValue(key, out var cached))
            return cached;

        var tileDef = (ContentTileDefinition) TilDefMan[currentTile.Tile.Tile.TypeId];
        var result = TileZRoof.HasZRoof(currentTile.Grid, currentTile.Tile.GridIndices, tileDef.HasZRoof);
        _currentRoofCache[key] = result;
        return result;
    }

    private bool HasOpaqueLinkedTileAbove(
        Entity<CEZLevelMapComponent> mapAboveUid,
        Entity<MapGridComponent> lowerGrid,
        Vector2 worldPosition,
        out bool foundLinkedGrid)
    {
        foundLinkedGrid = false;

        if (!_motionLinkQuery.TryComp(lowerGrid.Owner, out var lowerLink) ||
            string.IsNullOrEmpty(lowerLink.GroupId) ||
            !_mapQuery.TryComp(mapAboveUid.Owner, out var mapComp))
            return false;

        _opaqueProbeGrids.Clear();
        const float probeHalfExtents = 0.01f;
        var probeBounds = new Box2(
            worldPosition - new Vector2(probeHalfExtents, probeHalfExtents),
            worldPosition + new Vector2(probeHalfExtents, probeHalfExtents));
        _mapManager.FindGridsIntersecting(mapComp.MapId, probeBounds, ref _opaqueProbeGrids, approx: true, includeMap: false);

        foreach (var grid in _opaqueProbeGrids)
        {
            if (!_motionLinkQuery.TryComp(grid.Owner, out var upperLink) ||
                upperLink.GroupId != lowerLink.GroupId)
                continue;

            foundLinkedGrid = true;

            if (!_map.TryGetTileRef(grid.Owner, grid.Comp, worldPosition, out var tileRef) ||
                tileRef.Tile.IsEmpty)
                continue;

            var tileDef = (ContentTileDefinition)TilDefMan[tileRef.Tile.TypeId];
            if (!tileDef.Transparent)
                return true;
        }

        return false;
    }
}

public sealed partial class CEToggleZLevelLookUpAction : InstantActionEvent
{
}
