using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Content.Shared._Onyx.ZLevels;

namespace Content.Client._Onyx.ZLevels;

internal static class ZLevelFloodFillHelper
{
    public static HashSet<Vector2i> FindInteriorHoles(
        SharedMapSystem mapSystem,
        Entity<MapGridComponent> grid,
        ITileDefinitionManager tileDef)
    {
        return Shared._Onyx.ZLevels.ZLevelFloodFillHelper.FindInteriorHoles(mapSystem, grid, tileDef);
    }

    public static HashSet<Vector2i> FindInteriorHoles(
        SharedMapSystem mapSystem,
        Entity<MapGridComponent> grid)
    {
        return Shared._Onyx.ZLevels.ZLevelFloodFillHelper.FindInteriorHoles(mapSystem, grid);
    }

    public static HashSet<Vector2i> FindInteriorHolesFromSolid(HashSet<Vector2i> solidTiles)
    {
        return Shared._Onyx.ZLevels.ZLevelFloodFillHelper.FindInteriorHolesFromSolid(solidTiles);
    }
}
