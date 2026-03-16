using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<float> ZImpactVelocityLimit =
        CVarDef.Create("zlevels.impact_velocity_limit", 0.75f, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    public static readonly CVarDef<int> MaxZLevelsBelowRendering =
        CVarDef.Create("zlevels.max_z_levels_below_rendering", 3, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    public static readonly CVarDef<float> ZLevelOffset =
        CVarDef.Create("zlevels.z_level_offset", 0f, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    public static readonly CVarDef<float> ZLevelsAtmosTransferSpeed =
        CVarDef.Create("zlevels.atmos_transfer_speed", 1f, CVar.SERVERONLY | CVar.ARCHIVE);
}
