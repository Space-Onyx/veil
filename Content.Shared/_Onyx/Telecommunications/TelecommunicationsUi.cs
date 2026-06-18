using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.Telecommunications;

[Serializable, NetSerializable]
public enum TelecomSignalStatus : byte
{
    Routed,
    Garbled,
    RoutingDisabled,
    NoRoute,
    NoReceiver,
    NoBus,
    NoProcessor,
    NoBroadcaster,
    ChannelDisabled,
}

[Serializable, NetSerializable]
public enum TelecomTrafficConsoleUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class TelecomTrafficServerInfo(NetEntity entity, string name, bool routingEnabled)
{
    public readonly NetEntity Entity = entity;
    public readonly string Name = name;
    public readonly bool RoutingEnabled = routingEnabled;
}

[Serializable, NetSerializable]
public sealed class TelecomTrafficChannelInfo(string id, string name, bool enabled)
{
    public readonly string Id = id;
    public readonly string Name = name;
    public readonly bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class TelecomTrafficLogInfo(
    string timestamp,
    string channel,
    string source,
    string message,
    TelecomSignalStatus status,
    int messageLength,
    double timestampSeconds)
{
    public readonly string Timestamp = timestamp;
    public readonly string Channel = channel;
    public readonly string Source = source;
    public readonly string Message = message;
    public readonly TelecomSignalStatus Status = status;
    public readonly int MessageLength = messageLength;
    public readonly double TimestampSeconds = timestampSeconds;
}

[Serializable, NetSerializable]
public sealed class TelecomTrafficConsoleState(
    List<TelecomTrafficServerInfo> servers,
    NetEntity? selectedServer,
    bool routingEnabled,
    List<TelecomTrafficChannelInfo> selectedChannels,
    List<TelecomTrafficLogInfo> logs,
    int trafficBins,
    int trafficBinSeconds,
    double currentTimeSeconds) : BoundUserInterfaceState
{
    public readonly List<TelecomTrafficServerInfo> Servers = servers;
    public readonly NetEntity? SelectedServer = selectedServer;
    public readonly bool RoutingEnabled = routingEnabled;
    public readonly List<TelecomTrafficChannelInfo> SelectedChannels = selectedChannels;
    public readonly List<TelecomTrafficLogInfo> Logs = logs;
    public readonly int TrafficBins = trafficBins;
    public readonly int TrafficBinSeconds = trafficBinSeconds;
    public readonly double CurrentTimeSeconds = currentTimeSeconds;
}

[Serializable, NetSerializable]
public sealed class TelecomTrafficSelectServerMessage(NetEntity server) : BoundUserInterfaceMessage
{
    public readonly NetEntity Server = server;
}

[Serializable, NetSerializable]
public sealed class TelecomTrafficToggleRoutingMessage(bool enabled) : BoundUserInterfaceMessage
{
    public readonly bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class TelecomTrafficToggleChannelMessage(string channel) : BoundUserInterfaceMessage
{
    public readonly string Channel = channel;
}
