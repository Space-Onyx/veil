using System;
using System.Diagnostics.CodeAnalysis;
using Content.Shared._Onyx.Discord;
using Robust.Shared.Network;

namespace Content.Client._Onyx.Discord;

public sealed class DiscordIdManager
{
    [Dependency] private readonly IClientNetManager _netMgr = default!;

    private string? _discordId;
    private string? _discordUsername;

    public event Action? DiscordInfoUpdated;

    public void Initialize()
    {
        _netMgr.RegisterNetMessage<MsgDiscordIdInfo>(OnDiscordIdInfo);
        _netMgr.RegisterNetMessage<MsgDiscordUnlinkRequest>();
    }

    private void OnDiscordIdInfo(MsgDiscordIdInfo msg)
    {
        _discordId = msg.DiscordId;
        _discordUsername = msg.DiscordUsername;
        DiscordInfoUpdated?.Invoke();
    }

    public bool TryGetDiscordId([NotNullWhen(true)] out string? discordId)
    {
        discordId = _discordId;
        return _discordId != null;
    }

    public bool TryGetDiscordUsername([NotNullWhen(true)] out string? username)
    {
        username = _discordUsername;
        return _discordUsername != null;
    }

    public void RequestUnlink()
    {
        _netMgr.ClientSendMessage(new MsgDiscordUnlinkRequest());
    }
}
