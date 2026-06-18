using Content.Server._Onyx.Telecommunications.Components;
using Content.Shared._Onyx.Telecommunications;
using Content.Shared.DeviceLinking;
using Content.Shared.Labels.Components;
using Content.Shared.Lock;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server._Onyx.Telecommunications;

public sealed class TelecomTrafficConsoleSystem : EntitySystem
{
    private const float BaseUpdateInterval = 5f;
    private const int TrafficBins = 12;
    private static readonly TimeSpan BinDuration = TimeSpan.FromSeconds(5);

    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly LockSystem _lock = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly TelecommunicationsChainSystem _chain = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TelecomTrafficConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<TelecomTrafficConsoleComponent, TelecomTrafficSelectServerMessage>(OnSelectServer);
        SubscribeLocalEvent<TelecomTrafficConsoleComponent, TelecomTrafficToggleRoutingMessage>(OnToggleRouting);
        SubscribeLocalEvent<TelecomTrafficConsoleComponent, TelecomTrafficToggleChannelMessage>(OnToggleChannel);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<TelecomTrafficConsoleComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            component.UpdateAccumulator += frameTime;
            var updateInterval = GetTelemetryInterval(component.SelectedServer);
            if (component.UpdateAccumulator < updateInterval)
                continue;

            component.UpdateAccumulator = 0f;
            UpdateUi((uid, component));
        }
    }

    private void OnUiOpened(Entity<TelecomTrafficConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnSelectServer(
        Entity<TelecomTrafficConsoleComponent> ent,
        ref TelecomTrafficSelectServerMessage args)
    {
        if (_lock.IsLocked(ent.Owner))
            return;

        var server = GetEntity(args.Server);
        if (!IsServerVisible(ent.Owner, server))
            return;

        ent.Comp.SelectedServer = server;
        UpdateUi(ent);
    }

    private void OnToggleRouting(
        Entity<TelecomTrafficConsoleComponent> ent,
        ref TelecomTrafficToggleRoutingMessage args)
    {
        if (_lock.IsLocked(ent.Owner) ||
            ent.Comp.SelectedServer is not { } server ||
            !IsServerVisible(ent.Owner, server) ||
            !TryComp<TelecomSignalLogComponent>(server, out var log))
        {
            return;
        }

        if (!args.Enabled)
        {
            log.RoutingEnabled = false;
            if (TryComp<TelecomRouterComponent>(server, out var router))
                router.ResetArmed = true;
        }
        else
        {
            if (TryComp<TelecomRouterComponent>(server, out var router) &&
                router.ResetArmed)
            {
                _chain.RepairRouter(server);
            }

            log.RoutingEnabled = true;
        }

        UpdateUi(ent);
    }

    private void OnToggleChannel(
        Entity<TelecomTrafficConsoleComponent> ent,
        ref TelecomTrafficToggleChannelMessage args)
    {
        if (_lock.IsLocked(ent.Owner) ||
            ent.Comp.SelectedServer is not { } server ||
            !IsServerVisible(ent.Owner, server) ||
            !TryComp<TelecomRouterComponent>(server, out var router) ||
            !_chain.ServerHasChannel(server, args.Channel))
        {
            return;
        }

        if (!router.DisabledChannels.Add(args.Channel))
            router.DisabledChannels.Remove(args.Channel);

        UpdateUi(ent);
    }

    private void UpdateUi(Entity<TelecomTrafficConsoleComponent> ent)
    {
        var servers = GetServers(ent.Owner);

        if (ent.Comp.SelectedServer == null ||
            servers.All(server => GetEntity(server.Entity) != ent.Comp.SelectedServer))
        {
            ent.Comp.SelectedServer = servers.Count > 0
                ? GetEntity(servers[0].Entity)
                : null;
        }

        var logs = new List<TelecomTrafficLogInfo>();
        var selectedChannels = new List<TelecomTrafficChannelInfo>();
        var routingEnabled = false;
        var estimatedCalibration = 0;
        var estimatedLoad = 0;
        var telemetryInterval = (int) BaseUpdateInterval;

        if (ent.Comp.SelectedServer is { } selected &&
            TryComp<TelecomSignalLogComponent>(selected, out var log))
        {
            routingEnabled = log.RoutingEnabled;
            var metrics = _chain.GetServerMetrics(selected);
            estimatedCalibration = (int) MathF.Round(metrics.Quality * 100f);
            estimatedLoad = (int) MathF.Round(metrics.MaxUtilization * 100f);
            telemetryInterval = (int) MathF.Ceiling(GetTelemetryInterval(selected));

            var disabledChannels = TryComp<TelecomRouterComponent>(selected, out var router)
                ? router.DisabledChannels
                : [];

            foreach (var channel in _chain.GetServerChannels(selected)
                         .Select(id => (
                             Id: id,
                             Name: _prototypes.TryIndex<RadioChannelPrototype>(id, out var prototype)
                                 ? prototype.LocalizedName
                                 : id))
                         .OrderBy(channel => channel.Name))
            {
                selectedChannels.Add(new TelecomTrafficChannelInfo(
                    channel.Id,
                    channel.Name,
                    !disabledChannels.Contains(channel.Id)));
            }

            foreach (var entry in log.Entries.TakeLast(100).Reverse())
            {
                logs.Add(new TelecomTrafficLogInfo(
                    FormatTime(entry.Timestamp),
                    entry.Channel,
                    entry.Message,
                    SanitizeStatus(entry.Status),
                    entry.MessageLength,
                    entry.Timestamp.TotalSeconds,
                    entry.SignalQuality,
                    entry.LoadPercent,
                    entry.LatencyMilliseconds));
            }
        }

        _ui.SetUiState(
            ent.Owner,
            TelecomTrafficConsoleUiKey.Key,
            new TelecomTrafficConsoleState(
                servers,
                ent.Comp.SelectedServer is { } uid ? GetNetEntity(uid) : null,
                routingEnabled,
                selectedChannels,
                logs,
                TrafficBins,
                (int) BinDuration.TotalSeconds,
                _timing.CurTime.TotalSeconds,
                estimatedCalibration,
                estimatedLoad,
                telemetryInterval));
    }

    private List<TelecomTrafficServerInfo> GetServers(EntityUid console)
    {
        var result = new List<TelecomTrafficServerInfo>();
        if (!TryComp<DeviceLinkSinkComponent>(console, out var sink))
            return result;

        foreach (var serverUid in sink.LinkedSources)
        {
            if (!_chain.IsServerLinkedToConsole(serverUid, console) ||
                !TryComp<TelecomSignalLogComponent>(serverUid, out var log))
                continue;

            var name = GetServerDisplayName(serverUid);

            result.Add(new TelecomTrafficServerInfo(
                GetNetEntity(serverUid),
                name,
                log.RoutingEnabled));
        }

        return result;
    }

    private string GetServerDisplayName(EntityUid serverUid)
    {
        if (TryComp<LabelComponent>(serverUid, out var label) &&
            !string.IsNullOrWhiteSpace(label.CurrentLabel))
            return label.CurrentLabel;

        return Loc.GetString("telecom-server-label-unassigned");
    }

    private bool IsServerVisible(EntityUid console, EntityUid server)
    {
        return Transform(console).MapID == Transform(server).MapID &&
               HasComp<TelecomServerComponent>(server) &&
               HasComp<TelecomSignalLogComponent>(server) &&
               _chain.IsServerLinkedToConsole(server, console);
    }

    private static string FormatTime(TimeSpan time)
    {
        return $"{(int) time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
    }

    private float GetTelemetryInterval(EntityUid? server)
    {
        if (server == null || !Exists(server.Value))
            return BaseUpdateInterval;

        var quality = _chain.GetServerMetrics(server.Value).Quality;
        return BaseUpdateInterval + (1f - quality) * 15f;
    }

    private static TelecomSignalStatus SanitizeStatus(TelecomSignalStatus status)
    {
        return status switch
        {
            TelecomSignalStatus.NoReceiver or
            TelecomSignalStatus.NoBus or
            TelecomSignalStatus.NoProcessor or
            TelecomSignalStatus.NoBroadcaster or
            TelecomSignalStatus.NoRoute => TelecomSignalStatus.SignalLoss,
            _ => status,
        };
    }
}
