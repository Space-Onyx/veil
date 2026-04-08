using System;
using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<float> ZImpactVelocityLimit =
        CVarDef.Create("zlevels.impact_velocity_limit", 0.75f, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    public static readonly CVarDef<bool> ZLevelChasmFallEnabled =
        CVarDef.Create("zlevels.chasm_fall_enabled", false, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    public static readonly CVarDef<int> MaxZLevelsBelowRendering =
        CVarDef.Create("zlevels.max_z_levels_below_rendering", 2, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<float> ZLevelOffset =
        CVarDef.Create("zlevels.z_level_offset", 0f, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    public static readonly CVarDef<float> ZLevelHoleShadowOpacity =
        CVarDef.Create("zlevels.hole_shadow_opacity", 0.6f, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    public static readonly CVarDef<string> ZLevelHoleShadowColor =
        CVarDef.Create("zlevels.hole_shadow_color", "#000000", CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    public static readonly CVarDef<bool> ZLevelHoleShadowEnabled =
        CVarDef.Create("zlevels.hole_shadow_enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> ZLevelRoofOverlayEnabled =
        CVarDef.Create("zlevels.roof_overlay_enabled", true, CVar.CLIENTONLY);

    public static readonly CVarDef<int> ZLevelHoleShadowUpdateRate =
        CVarDef.Create("zlevels.hole_shadow_update_rate", 20, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<float> ZLevelHoleShadowMaxDistance =
        CVarDef.Create("zlevels.hole_shadow_max_distance", 0f, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<float> ZLevelsAtmosTransferSpeed =
        CVarDef.Create("zlevels.atmos_transfer_speed", 5f, CVar.SERVERONLY | CVar.ARCHIVE);
}
