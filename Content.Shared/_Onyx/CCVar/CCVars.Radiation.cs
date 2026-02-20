using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Должна ли радиация вызывать мутации.
    /// </summary>
    public static readonly CVarDef<bool> RadiationEnableMutations =
        CVarDef.Create("radiation.enable_mutations", true, CVar.SERVERONLY);
}