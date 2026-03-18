/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Actions;
using Content.Shared.Maps;
using Robust.Shared.Map;

namespace Content.Shared._CE.ZLevels.Core.EntitySystems;

public abstract partial class CESharedZLevelsSystem
{
    [Dependency] protected readonly ITileDefinitionManager TilDefMan = default!;
    private void InitView()
    {
        SubscribeLocalEvent<CEZLevelViewerComponent, MoveEvent>(OnViewerMove);
        SubscribeLocalEvent<CEZLevelViewerComponent, CEToggleZLevelLookUpAction>(OnToggleLookUp);
        SubscribeLocalEvent<TileChangedEvent>(OnViewTileChanged); // <Onyx-Tweak>
    }

    // <Onyx-Tweak>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        UpdateView();

        sw.Restart();
        UpdateMovement(frameTime);
    }

    private void UpdateView()
    {
        if (_opaqueAboveCacheDirty)
        {
            _opaqueAboveCache.Clear();
            _opaqueAboveCacheDirty = false;
        }
    }

    private void OnViewTileChanged(ref TileChangedEvent ev)
    {
        _opaqueAboveCacheDirty = true;
        _groundCacheGeneration++;
        // <Onyx-Tweak>
        foreach (var change in ev.Changes)
        {
            WakeEntitiesOnTile(ev.Entity, change.GridIndices);
        }
        // </Onyx-Tweak>
    }
    // </Onyx-Tweak>

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

    // <Onyx-Tweak>
    private readonly Dictionary<(EntityUid MapAbove, Vector2i TilePos), bool> _opaqueAboveCache = new();
    private bool _opaqueAboveCacheDirty;
    // </Onyx-Tweak>
    public bool HasOpaqueAbove(EntityUid ent, Entity<CEZLevelMapComponent?>? currentMapUid = null)
    {
        currentMapUid ??= Transform(ent).MapUid;

        if (currentMapUid is null)
            return false;

        if (!TryMapUp(currentMapUid.Value, out var mapAboveUid))
            return false;

        if (!_mapQuery.TryComp(mapAboveUid.Value, out var mapComp)) // <Onyx-Tweak>
            return false;

        var worldPosition = _transform.GetWorldPosition(ent);
        // <Onyx-Tweak>
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
        // </Onyx-Tweak>
    }
}

public sealed partial class CEToggleZLevelLookUpAction : InstantActionEvent
{
}
