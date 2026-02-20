using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Должно ли голосование за карту автоматически начинаться после перезапуска раунда.
    /// </summary>
    public static readonly CVarDef<bool> VoteMapAutoAfterRestart =
        CVarDef.Create("vote.map_auto_after_restart", false, CVar.SERVERONLY);

    /// <summary>
    ///     Должно ли голосование за режим автоматически начинаться после перезапуска раунда.
    /// </summary>
    public static readonly CVarDef<bool> VotePresetAutoAfterRestart =
        CVarDef.Create("vote.preset_auto_after_restart", false, CVar.SERVERONLY);
}