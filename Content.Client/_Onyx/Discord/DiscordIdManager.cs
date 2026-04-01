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
    private string? _linkCode;

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
        _linkCode = msg.LinkCode;
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

    public bool TryGetLinkCode([NotNullWhen(true)] out string? linkCode)
    {
        linkCode = _linkCode;
        return _linkCode != null;
    }

    public void RequestUnlink()
    {
        _netMgr.ClientSendMessage(new MsgDiscordUnlinkRequest());
    }

    public void RequestDiscordInfo()
    {
        _netMgr.ClientSendMessage(new MsgDiscordIdInfo());
    }
}
