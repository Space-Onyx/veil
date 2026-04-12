using Content.Server.Popups;
using Content.Shared._Onyx.ZLevels;
using Content.Shared._Onyx.ZLevels.Components;
using Content.Shared._Onyx.ZLevels.Core.Components;
using Content.Shared._Onyx.ZLevels.Core.EntitySystems;
using Content.Shared._Utopia.ZLevels.Components;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Tools.Components;
using Content.Shared.Verbs;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Map.Components;

namespace Content.Server._Onyx.ZLevels.Systems;

public sealed class ZCeilingPrySystem : EntitySystem
{
    [Dependency] private readonly CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly TileSystem _tile = default!;
    [Dependency] private readonly ITileDefinitionManager _tileDefManager = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityQuery<GridMotionLinkComponent> _motionLinkQuery;
    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<MapComponent> _mapQuery;

    private const float BaseDelay = 6f;

    public override void Initialize()
    {
        base.Initialize();

        _motionLinkQuery = GetEntityQuery<GridMotionLinkComponent>();
        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _mapQuery = GetEntityQuery<MapComponent>();

        SubscribeLocalEvent<ZCeilingCutterComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<ZCeilingCutterComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ZCeilingCutterComponent, ZCeilingPryDoAfterEvent>(OnDoAfterComplete);
    }

    private void OnGetVerbs(Entity<ZCeilingCutterComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var user = args.User;
        var tool = ent;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = tool.Comp.CeilingMode
                ? Loc.GetString("z-ceiling-cutter-verb-disable")
                : Loc.GetString("z-ceiling-cutter-verb-enable"),
            Act = () =>
            {
                tool.Comp.CeilingMode = !tool.Comp.CeilingMode;
                Dirty(tool);

                var msg = tool.Comp.CeilingMode
                    ? Loc.GetString("z-ceiling-cutter-mode-on")
                    : Loc.GetString("z-ceiling-cutter-mode-off");
                _popup.PopupEntity(msg, user, user);
            },
            Priority = 2,
        });
    }

    private void OnAfterInteract(Entity<ZCeilingCutterComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled)
            return;

        if (!ent.Comp.CeilingMode)
            return;

        if (args.Target != null)
            return;

        if (!args.CanReach)
            return;

        if (TryComp<WelderComponent>(ent, out var welder) && !welder.Enabled)
            return;

        if (!TryComp<ToolComponent>(ent, out var tool))
            return;

        var user = args.User;
        var userXform = Transform(user);

        if (userXform.MapUid is not { } mapUid)
            return;

        if (!_zLevels.TryMapUp((mapUid, null), out var aboveMap))
            return;

        if (!_mapManager.TryFindGridAt(
                _transform.GetMapId(userXform.Coordinates),
                _transform.GetWorldPosition(userXform),
                out var gridUid, out var gridComp))
            return;

        var tilePos = _mapSystem.CoordinatesToTile(gridUid, gridComp, args.ClickLocation);

        if (!TryGetCeilingTile(gridUid, tilePos, aboveMap.Value,
                out var ceilingGrid, out var ceilingGridComp, out var ceilingTilePos))
        {
            _popup.PopupEntity(Loc.GetString("z-ceiling-pry-no-ceiling"), user, user);
            args.Handled = true;
            return;
        }

        if (!_mapSystem.TryGetTileRef(ceilingGrid, ceilingGridComp, ceilingTilePos, out var ceilingTileRef)
            || ceilingTileRef.Tile.IsEmpty)
        {
            _popup.PopupEntity(Loc.GetString("z-ceiling-pry-no-ceiling"), user, user);
            args.Handled = true;
            return;
        }

        var tileDef = (ContentTileDefinition) _tileDefManager[ceilingTileRef.Tile.TypeId];
        if (tileDef.Indestructible)
        {
            _popup.PopupEntity(Loc.GetString("z-ceiling-pry-no-ceiling"), user, user);
            args.Handled = true;
            return;
        }

        if (IsCeilingTileBlocked(ceilingGrid, ceilingGridComp, ceilingTilePos))
        {
            _popup.PopupEntity(Loc.GetString("z-ceiling-pry-blocked"), user, user);
            args.Handled = true;
            return;
        }

        var delay = BaseDelay / tool.SpeedModifier;

        var ev = new ZCeilingPryDoAfterEvent(GetNetEntity(ceilingGrid), ceilingTilePos);

        var doAfterArgs = new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(delay),
            ev,
            eventTarget: ent,
            used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(doAfterArgs))
        {
            _popup.PopupEntity(Loc.GetString("z-ceiling-pry-start"), user, user);
        }

        args.Handled = true;
    }

    private void OnDoAfterComplete(Entity<ZCeilingCutterComponent> ent, ref ZCeilingPryDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        var ceilingGrid = GetEntity(args.CeilingGrid);
        if (!_gridQuery.TryComp(ceilingGrid, out var gridComp))
            return;

        for (var i = 0; i < 10; i++)
        {
            if (!_mapSystem.TryGetTileRef(ceilingGrid, gridComp, args.CeilingTilePos, out var tileRef)
                || tileRef.Tile.IsEmpty)
                break;

            var tileDef = (ContentTileDefinition) _tileDefManager[tileRef.Tile.TypeId];
            if (tileDef.Indestructible)
                break;

            if (!_tile.DeconstructTile(tileRef))
                break;
        }

        if (args.User is { } user)
            _popup.PopupEntity(Loc.GetString("z-ceiling-pry-success"), user, user);

        args.Handled = true;
    }

    private bool TryGetCeilingTile(
        EntityUid currentGrid,
        Vector2i tilePos,
        Entity<CEZLevelMapComponent> aboveMap,
        out EntityUid ceilingGrid,
        out MapGridComponent ceilingGridComp,
        out Vector2i ceilingTilePos)
    {
        ceilingGrid = default;
        ceilingGridComp = default!;
        ceilingTilePos = default;

        if (!_gridQuery.TryComp(currentGrid, out var currentGridComp))
            return false;

        var worldPos = _mapSystem.GridTileToWorldPos(currentGrid, currentGridComp, tilePos);

        string? sourceGroupId = null;
        if (_motionLinkQuery.TryComp(currentGrid, out var sourceLink))
            sourceGroupId = sourceLink.GroupId;

        if (!_mapQuery.TryComp(aboveMap, out var aboveMapComp))
            return false;

        if (sourceGroupId != null)
        {
            var query = EntityQueryEnumerator<GridMotionLinkComponent, MapGridComponent, TransformComponent>();
            while (query.MoveNext(out var gridUid, out var link, out var grid, out var xform))
            {
                if (xform.MapUid != aboveMap.Owner)
                    continue;

                if (link.GroupId != sourceGroupId)
                    continue;

                var aboveTilePos = _mapSystem.WorldToTile(gridUid, grid, worldPos);

                if (_mapSystem.TryGetTileRef(gridUid, grid, aboveTilePos, out var tileRef) && !tileRef.Tile.IsEmpty)
                {
                    ceilingGrid = gridUid;
                    ceilingGridComp = grid;
                    ceilingTilePos = aboveTilePos;
                    return true;
                }
            }
        }

        if (_mapManager.TryFindGridAt(aboveMapComp.MapId, worldPos, out var fallbackGridUid, out var fallbackGrid))
        {
            var aboveTilePos = _mapSystem.WorldToTile(fallbackGridUid, fallbackGrid, worldPos);
            if (_mapSystem.TryGetTileRef(fallbackGridUid, fallbackGrid, aboveTilePos, out var tileRef) && !tileRef.Tile.IsEmpty)
            {
                ceilingGrid = fallbackGridUid;
                ceilingGridComp = fallbackGrid;
                ceilingTilePos = aboveTilePos;
                return true;
            }
        }

        return false;
    }

    private bool IsCeilingTileBlocked(EntityUid gridUid, MapGridComponent grid, Vector2i tilePos)
    {
        foreach (var ent in _mapSystem.GetAnchoredEntities(gridUid, grid, tilePos))
        {
            if (!_fixturesQuery.TryComp(ent, out var fixtures))
                continue;

            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if ((fixture.CollisionLayer & (int) CollisionGroup.Impassable) != 0)
                    return true;
            }
        }

        return false;
    }
}
