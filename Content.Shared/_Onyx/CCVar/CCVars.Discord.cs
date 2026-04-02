using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    /// Вебхук банов с сервера сски
    /// </summary>
    public static readonly CVarDef<string> DiscordBansWebhook =
        CVarDef.Create("discord.bans_webhook", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    /// Ссылка на канал привязки аккаунта сски к дискорду
    /// </summary>
    public static readonly CVarDef<string> DiscordLinkChannel =
        CVarDef.Create("discord.link_channel", string.Empty, CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// Хранит токен бота Discord для авторизации при взаимодействии с Discord API.
    /// Этот токен используется для выполнения операций от имени бота, таких как получение информации о пользователях.
    /// Токен должен быть передан в строковом формате.
    /// </summary>
    public static readonly CVarDef<string> DiscordTokenBot =
        CVarDef.Create("discord.token_bot", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL | CVar.ARCHIVE);

    /// <summary>
    /// Включает или отключает авторизацию через Discord.
    /// </summary>
    public static readonly CVarDef<bool> DiscordAuthEnable =
        CVarDef.Create("discord.auth_enable", false, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// Отвечает за обязательность привязки аккаунта Discord.
    /// </summary>
    public static readonly CVarDef<bool> DiscordAuthLinkRequired =
        CVarDef.Create("discord.auth_link_required", false, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// Включает или отключает отображение дополнительной лобби-панели в пользовательском интерфейсе.
    /// При значении true панель отображается, при false - скрывается.
    /// </summary>
    public static readonly CVarDef<bool> ExtraLobbyPanelEnabled =
        CVarDef.Create("ui.show_lobby_panel", true, CVar.REPLICATED | CVar.SERVER);
}