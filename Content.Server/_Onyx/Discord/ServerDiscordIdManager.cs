using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Content.Server._Onyx.Administration;
using Content.Server.Database;
using Content.Shared.CCVar;
using Content.Shared._Onyx.Discord;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Onyx.Discord;

public sealed class ServerDiscordIdManager : EntitySystem
{
    private static readonly TimeSpan LinkCodeLifetime = TimeSpan.FromMinutes(5);

    [Dependency] private readonly IServerNetManager _net = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private readonly Dictionary<NetUserId, string?> _cachedDiscordIds = new();
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("discord-id");

        _net.RegisterNetMessage<MsgDiscordIdInfo>(OnDiscordIdInfoRequest);
        _net.RegisterNetMessage<MsgDiscordUnlinkRequest>(OnDiscordUnlinkRequest);

        _players.PlayerStatusChanged += OnPlayerStatusChanged;
        _net.Disconnect += OnDisconnected;
    }

    private void OnDisconnected(object? sender, NetDisconnectedArgs e)
    {
        var userId = e.Channel.UserId;
        _cachedDiscordIds.Remove(userId);
    }

    private async void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.InGame)
            return;

        var session = args.Session;
        var userId = session.UserId;

        if (_cachedDiscordIds.ContainsKey(userId))
        {
            _sawmill.Warning($"Discord ID for {userId} already cached at InGame. Overwriting.");
        }

        await SendDiscordInfo(session.Channel);
    }

    private async Task<string?> LoadDiscordId(NetUserId userId)
    {
        try
        {
            var discordId = await _db.GetDiscordIdAsync(userId.UserId);
            return discordId;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Failed to load Discord ID for {userId}: {ex}");
            return null;
        }
    }

    public bool TryGetDiscordId(NetUserId userId, [NotNullWhen(true)] out string? discordId)
    {
        return _cachedDiscordIds.TryGetValue(userId, out discordId);
    }

    public void InvalidateCache(NetUserId userId)
    {
        _cachedDiscordIds.Remove(userId);
    }

    public void SetDiscordId(NetUserId userId, string? discordId)
    {
        _cachedDiscordIds[userId] = discordId;
    }

    private async void OnDiscordIdInfoRequest(MsgDiscordIdInfo msg)
    {
        await SendDiscordInfo(msg.MsgChannel);
    }

    private async Task SendDiscordInfo(INetChannel channel)
    {
        var userId = channel.UserId;
        if (!_cfg.GetCVar(CCVars.DiscordAuthEnable))
        {
            _cachedDiscordIds[userId] = null;
            _net.ServerSendMessage(new MsgDiscordIdInfo
            {
                UserId = userId,
                DiscordId = null,
                DiscordUsername = null,
                LinkCode = null
            }, channel);
            return;
        }

        var discordId = await LoadDiscordId(userId);
        _cachedDiscordIds[userId] = discordId;
        string? linkCode = null;

        string? discordUsername = null;

        if (discordId != null)
        {
            if (ulong.TryParse(discordId, out var discordUlong))
            {
                try
                {
                    var botToken = _cfg.GetCVar(CCVars.DiscordTokenBot);
                    discordUsername = await AuthApiHelper.GetAccountDiscord(discordUlong, botToken);
                }
                catch (Exception ex)
                {
                    _sawmill.Error($"Failed to fetch Discord username for {discordId}: {ex}");
                }
            }

            try
            {
                await _db.RemoveDiscordLinkCodeAsync(userId.UserId);
            }
            catch (Exception ex)
            {
                _sawmill.Error($"Failed to remove stale Discord link code for {userId}: {ex}");
            }
        }
        else
        {
            try
            {
                linkCode = await _db.GetOrCreateDiscordLinkCodeAsync(userId.UserId, LinkCodeLifetime);
            }
            catch (Exception ex)
            {
                _sawmill.Error($"Failed to get Discord link code for {userId}: {ex}");
            }
        }

        var msg = new MsgDiscordIdInfo
        {
            UserId = userId,
            DiscordId = discordId,
            DiscordUsername = discordUsername,
            LinkCode = linkCode
        };

        _net.ServerSendMessage(msg, channel);
    }

    private async void OnDiscordUnlinkRequest(MsgDiscordUnlinkRequest msg)
    {
        var userId = msg.MsgChannel.UserId;
        if (!_cfg.GetCVar(CCVars.DiscordAuthEnable))
        {
            await SendDiscordInfo(msg.MsgChannel);
            return;
        }

        try
        {
            await _db.UnlinkDiscordIdAsync(userId.UserId);
            _cachedDiscordIds[userId] = null;
            string? linkCode = null;
            try
            {
                linkCode = await _db.GetOrCreateDiscordLinkCodeAsync(userId.UserId, LinkCodeLifetime);
            }
            catch (Exception codeEx)
            {
                _sawmill.Error($"Failed to create Discord link code after unlink for {userId}: {codeEx}");
            }

            var response = new MsgDiscordIdInfo
            {
                UserId = userId,
                DiscordId = null,
                DiscordUsername = null,
                LinkCode = linkCode
            };

            _net.ServerSendMessage(response, msg.MsgChannel);

            if (_cfg.GetCVar(CCVars.DiscordAuthEnable) && _cfg.GetCVar(CCVars.DiscordAuthLinkRequired))
                _net.DisconnectChannel(msg.MsgChannel, "Отвязка дискорд аккаунта.");

            _sawmill.Info($"Discord account unlinked for {userId}.");
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Failed to unlink Discord for {userId}: {ex}");
        }
    }
}

