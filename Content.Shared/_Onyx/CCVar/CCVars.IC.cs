using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Включение OOC заметок для персонажей.
    /// </summary>
    public static readonly CVarDef<bool> OOCNotes =
        CVarDef.Create("ic.ooc_notes", false, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Устанавливает максимальную длину для OOC заметок.
    /// </summary>
    public static readonly CVarDef<int> MaxOOCNotesLength =
        CVarDef.Create("ic.ooc_notes_length", 128, CVar.SERVER | CVar.REPLICATED);
}