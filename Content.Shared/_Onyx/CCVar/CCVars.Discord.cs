using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    /// Вебхук для уведомлений о серверных банах.
    /// </summary>
    public static readonly CVarDef<string> DiscordBansWebhook =
        CVarDef.Create("discord.bans_webhook", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    /// Ссылка на Discord-канал с инструкциями по привязке аккаунта.
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
    /// Включает или отключает систему привязки аккаунта через Discord.
    /// </summary>
    public static readonly CVarDef<bool> DiscordAuthEnable =
        CVarDef.Create("discord.auth_enable", false, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// Если true, привязка Discord-аккаунта обязательна для входа.
    /// </summary>
    public static readonly CVarDef<bool> DiscordAuthLinkRequired =
        CVarDef.Create("discord.auth_link_required", false, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// URL endpoint'а DiscordAuthBot для глобальной отвязки из игрового UI.
    /// Пример: http://127.0.0.1:8088/api/v1/discord/unlink
    /// </summary>
    public static readonly CVarDef<string> DiscordAuthBotApiUrl =
        CVarDef.Create("discord.auth_bot_api_url", string.Empty, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Bearer-токен авторизации для API DiscordAuthBot.
    /// Должен совпадать с BOT_API_TOKEN в DiscordAuthBot.
    /// </summary>
    public static readonly CVarDef<string> DiscordAuthBotApiToken =
        CVarDef.Create("discord.auth_bot_api_token", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL | CVar.ARCHIVE);

    /// <summary>
    /// Таймаут (в секундах) для HTTP-запроса глобальной отвязки в DiscordAuthBot.
    /// </summary>
    public static readonly CVarDef<int> DiscordAuthBotApiTimeoutSeconds =
        CVarDef.Create("discord.auth_bot_api_timeout", 5, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Включает отображение дополнительной панели в лобби.
    /// </summary>
    public static readonly CVarDef<bool> ExtraLobbyPanelEnabled =
        CVarDef.Create("ui.show_lobby_panel", true, CVar.REPLICATED | CVar.SERVER);
}
