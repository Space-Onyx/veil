/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server._Onyx.ZLevels.Core;
using Content.Shared._Onyx.ZLevels.Core.Components;
using Content.Shared._Onyx.ZLevels.Roof;
using Content.Shared.Light.Components;
using Content.Shared.Maps;

namespace Content.Server._Onyx.ZLevels.Roof;

/// <inheritdoc/>
public sealed class CERoofSystem : CESharedRoofSystem
{
    private readonly HashSet<Vector2i> _roofMap = new();
    private readonly List<(int Depth, EntityUid MapUid)> _sortedMapsBuffer = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CEZLevelsNetworkComponent, CEZLevelNetworkUpdatedEvent>(OnNetworkUpdated);
    }

    private void OnNetworkUpdated(Entity<CEZLevelsNetworkComponent> ent, ref CEZLevelNetworkUpdatedEvent args)
    {
        RecalculateNetworkRoofs(ent);
    }

    public void RecalculateNetworkRoofs(Entity<CEZLevelsNetworkComponent> network)
    {
        _roofMap.Clear();
        _sortedMapsBuffer.Clear();

        foreach (var (depth, mapUid) in network.Comp.ZLevels)
        {
            if (mapUid.HasValue)
                _sortedMapsBuffer.Add((depth, mapUid.Value));
        }

        _sortedMapsBuffer.Sort((a, b) => b.Depth.CompareTo(a.Depth));

        foreach (var (_, map) in _sortedMapsBuffer)
        {
            if (!GridQuery.TryComp(map, out var mapGrid))
                continue;

            var enumerator = Map.GetAllTilesEnumerator(map, mapGrid);
            var roofComp = EnsureComp<RoofComponent>(map);

            while (enumerator.MoveNext(out var tileRef))
            {
                Roof.SetRoof((map, mapGrid, roofComp), tileRef.Value.GridIndices, _roofMap.Contains(tileRef.Value.GridIndices));

                var tileDef = (ContentTileDefinition)TilDefMan[tileRef.Value.Tile.TypeId];

                if (!tileDef.Transparent)
                    _roofMap.Add(tileRef.Value.GridIndices);
            }
        }
    }
}
