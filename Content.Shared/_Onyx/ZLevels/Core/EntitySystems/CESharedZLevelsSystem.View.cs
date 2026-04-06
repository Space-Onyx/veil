/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._Onyx.ZLevels.Core.Components;
using Content.Shared.Actions;
using Content.Shared.Maps;
using Robust.Shared.Map;

namespace Content.Shared._Onyx.ZLevels.Core.EntitySystems;

public abstract partial class CESharedZLevelsSystem
{
    [Dependency] protected readonly ITileDefinitionManager TilDefMan = default!;
    private static readonly TimeSpan NetworkCacheRefreshInterval = TimeSpan.FromSeconds(0.5f);
    private static readonly TimeSpan OpaqueAboveCacheCleanupInterval = TimeSpan.FromSeconds(5f);
    private const int OpaqueAboveCacheSoftLimit = 8192;
    private TimeSpan _nextNetworkCacheRefresh = TimeSpan.Zero;
    private TimeSpan _nextOpaqueAboveCacheCleanup = TimeSpan.Zero;
    private void InitView()
    {
        SubscribeLocalEvent<CEZLevelViewerComponent, MoveEvent>(OnViewerMove);
        SubscribeLocalEvent<CEZLevelViewerComponent, CEToggleZLevelLookUpAction>(OnToggleLookUp);
        SubscribeLocalEvent<TileChangedEvent>(OnViewTileChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime >= _nextNetworkCacheRefresh)
        {
            _nextNetworkCacheRefresh = _timing.CurTime + NetworkCacheRefreshInterval;
            _networkCacheDirty = true;
        }

        UpdateView();
        UpdateMovement(frameTime);
    }

    private void UpdateView()
    {
        if (_opaqueAboveCacheDirty)
        {
            _opaqueAboveCache.Clear();
            _opaqueAboveCacheDirty = false;
            _nextOpaqueAboveCacheCleanup = _timing.CurTime + OpaqueAboveCacheCleanupInterval;
            return;
        }

        if (_opaqueAboveCache.Count == 0)
            return;

        if (_opaqueAboveCache.Count > OpaqueAboveCacheSoftLimit ||
            _timing.CurTime >= _nextOpaqueAboveCacheCleanup)
        {
            _opaqueAboveCache.Clear();
            _nextOpaqueAboveCacheCleanup = _timing.CurTime + OpaqueAboveCacheCleanupInterval;
        }
    }

    private void OnViewTileChanged(ref TileChangedEvent ev)
    {
        _opaqueAboveCacheDirty = true;
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

    private readonly Dictionary<(EntityUid MapAbove, Vector2i TilePos), bool> _opaqueAboveCache = new();
    private bool _opaqueAboveCacheDirty;
    public bool HasOpaqueAbove(EntityUid ent, Entity<CEZLevelMapComponent?>? currentMapUid = null)
    {
        currentMapUid ??= Transform(ent).MapUid;

        if (currentMapUid is null)
            return false;

        if (!TryMapUp(currentMapUid.Value, out var mapAboveUid))
            return false;

        if (!_mapQuery.TryComp(mapAboveUid.Value, out var mapComp))
            return false;

        var worldPosition = _transform.GetWorldPosition(ent);
        var approxTile = new Vector2i((int)MathF.Floor(worldPosition.X), (int)MathF.Floor(worldPosition.Y));
        var cacheKey = (mapAboveUid.Value.Owner, approxTile);

        if (!_opaqueAboveCacheDirty && _opaqueAboveCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var result = false;
        foreach (var grid in GetCachedGrids(mapComp.MapId))
        {
            if (!_map.TryGetTileRef(grid.Owner, grid.Comp, worldPosition, out var tileRef))
                continue;

            if (tileRef.Tile.IsEmpty)
                continue;

            var tileDef = (ContentTileDefinition)TilDefMan[tileRef.Tile.TypeId];
            if (!tileDef.Transparent)
            {
                result = true;
                break;
            }
        }

        _opaqueAboveCache[cacheKey] = result;
        return result;
    }
}

public sealed partial class CEToggleZLevelLookUpAction : InstantActionEvent
{
}
