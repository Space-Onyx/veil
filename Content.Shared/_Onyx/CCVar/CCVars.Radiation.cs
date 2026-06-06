using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Должна ли радиация вызывать мутации.
    /// </summary>
    public static readonly CVarDef<bool> RadiationEnableMutations =
        CVarDef.Create("radiation.enable_mutations", true, CVar.SERVERONLY);

    /// <summary>
    ///     Множитель силы мутаций.
    /// </summary>
    public static readonly CVarDef<float> RadiationMutationStrengthModifier =
        CVarDef.Create("radiation.mutation_strength_modifier", 1.0f, CVar.SERVERONLY);
}
