using System.Collections.Generic;
using Content.Server._Onyx.ZLevels.Roof.Components;
using Content.Shared._Onyx.ZLevels.Roof.Components;
using Content.Shared._Onyx.ZLevels.Roof.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._Onyx.ZLevels.Roof;

public sealed class SetTileZRoofSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTileZRoofSystem _tileZRoof = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    private EntityQuery<MapGridComponent> _gridQuery;

    public override void Initialize()
    {
        _gridQuery = GetEntityQuery<MapGridComponent>();
        SubscribeLocalEvent<SetTileZRoofComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<SetTileZRoofComponent> ent, ref ComponentStartup args)
    {
        TryApply(ent);
        QueueDel(ent.Owner);
    }

    private void TryApply(Entity<SetTileZRoofComponent> ent)
    {
        var xform = Transform(ent.Owner);

        EntityUid? gridUid = null;
        if (xform.GridUid != null && _gridQuery.HasComp(xform.GridUid.Value))
            gridUid = xform.GridUid.Value;
        else if (_gridQuery.HasComp(xform.ParentUid))
            gridUid = xform.ParentUid;

        if (gridUid == null || !_gridQuery.TryComp(gridUid.Value, out var grid))
        {
            return;
        }

        var tileZRoof = EnsureComp<TileZRoofComponent>(gridUid.Value);
        var value = ent.Comp.Value ? (byte) 2 : (byte) 1;

        var indices = new HashSet<Vector2i>
        {
            _map.LocalToTile(gridUid.Value, grid, xform.Coordinates)
        };

        var worldPos = _xform.GetWorldPosition(ent.Owner);
        indices.Add(_map.WorldToTile(gridUid.Value, grid, worldPos));
        indices.Add(_map.CoordinatesToTile(gridUid.Value, grid, new MapCoordinates(worldPos, xform.MapID)));

        foreach (var index in indices)
        {
            _tileZRoof.SetHasZRoofOverride((gridUid.Value, grid, tileZRoof), index, value);
        }
    }
}
