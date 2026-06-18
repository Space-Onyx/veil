using Content.Server.Power.Components;
using Content.Server._Onyx.Telecommunications.Components;
using Content.Shared._Onyx.Telecommunications;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Emag.Systems;
using Content.Shared.Examine;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using System.Linq;

namespace Content.Server._Onyx.Telecommunications;

public sealed class TelecommunicationsChainSystem : EntitySystem
{
    private const string ReceiverOutputPort = "TelecomReceiverOutput";
    private const string ProcessorInputPort = "TelecomProcessorInput";
    private const string ProcessorOutputPort = "TelecomProcessorOutput";
    private const string BusInputPort = "TelecomBusInput";
    private const string BusOutputPort = "TelecomBusOutput";
    private const string ServerInputPort = "TelecomServerInput";
    private const string ServerOutputPort = "TelecomServerOutput";
    private const string BroadcasterInputPort = "TelecomBroadcasterInput";
    private const string ServerMonitorOutputPort = "TelecomServerMonitorOutput";
    private const string ConsoleInputPort = "TelecomConsoleInput";

    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly EmagSystem _emag = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TelecomSignalLogComponent, ExaminedEvent>(OnLogExamined);
        SubscribeLocalEvent<TelecomReceiverComponent, LinkAttemptEvent>(OnReceiverLinkAttempt);
        SubscribeLocalEvent<TelecomProcessorComponent, LinkAttemptEvent>(OnProcessorLinkAttempt);
        SubscribeLocalEvent<TelecomBusComponent, LinkAttemptEvent>(OnBusLinkAttempt);
        SubscribeLocalEvent<TelecomSignalLogComponent, LinkAttemptEvent>(OnServerLinkAttempt);
        SubscribeLocalEvent<TelecomBroadcasterComponent, LinkAttemptEvent>(OnBroadcasterLinkAttempt);
        SubscribeLocalEvent<TelecomTrafficConsoleComponent, LinkAttemptEvent>(OnConsoleLinkAttempt);
        SubscribeLocalEvent<TelecomRouterComponent, GotEmaggedEvent>(OnRouterEmagged);
        SubscribeLocalEvent<TelecomBroadcasterComponent, GotEmaggedEvent>(OnBroadcasterEmagged);
    }

    public TelecomRouteResult RouteSignal(
        MapId mapId,
        RadioChannelPrototype channel,
        EntityUid messageSource,
        string message)
    {
        if (!TryFindServer(mapId, channel.ID, out var server))
        {
            AddFallbackLog(mapId, channel, messageSource, message,
                TelecomSignalStatus.NoRoute, message.Length);
            return new TelecomRouteResult(false, message);
        }

        var log = EnsureComp<TelecomSignalLogComponent>(server);
        var chainStatus = GetChainStatus(server, mapId, out var broadcasterSabotaged);
        if (chainStatus != TelecomSignalStatus.Routed)
        {
            AddLogEntry(log, channel, messageSource, message,
                chainStatus, message.Length);
            return new TelecomRouteResult(false, message);
        }

        if (!log.RoutingEnabled)
        {
            AddLogEntry(log, channel, messageSource, message,
                TelecomSignalStatus.RoutingDisabled, message.Length);
            return new TelecomRouteResult(false, message);
        }

        if (!TryComp<TelecomRouterComponent>(server, out var router))
        {
            AddLogEntry(log, channel, messageSource, message,
                TelecomSignalStatus.NoRoute, message.Length);
            return new TelecomRouteResult(false, message);
        }

        if (router.DisabledChannels.Contains(channel.ID))
        {
            AddLogEntry(log, channel, messageSource, message,
                TelecomSignalStatus.ChannelDisabled, message.Length);
            return new TelecomRouteResult(false, message);
        }

        if (router.Sabotaged || broadcasterSabotaged)
        {
            var garbled = GarbleMessage(message);
            AddLogEntry(log, channel, messageSource, garbled,
                TelecomSignalStatus.Garbled, garbled.Length);
            return new TelecomRouteResult(true, garbled);
        }

        AddLogEntry(log, channel, messageSource, message,
            TelecomSignalStatus.Routed, message.Length);
        return new TelecomRouteResult(true, message);
    }

    public List<EntityUid> GetServers(MapId mapId)
    {
        var result = new List<EntityUid>();
        var query = EntityQueryEnumerator<TelecomServerComponent, TelecomSignalLogComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var transform))
        {
            if (transform.MapID == mapId)
                result.Add(uid);
        }

        return result;
    }

    public HashSet<string> GetServerChannels(EntityUid server)
    {
        return TryComp<EncryptionKeyHolderComponent>(server, out var holder)
            ? new HashSet<string>(holder.Channels)
            : [];
    }

    public bool ServerHasChannel(EntityUid server, string channel)
    {
        return GetServerChannels(server).Contains(channel);
    }

    public bool IsServerLinkedToConsole(EntityUid server, EntityUid console)
    {
        return TryComp<DeviceLinkSourceComponent>(server, out var source) &&
               IsLinked(source, console, ServerMonitorOutputPort, ConsoleInputPort);
    }

    private bool TryFindServer(MapId mapId, string channel, out EntityUid result)
    {
        var query = EntityQueryEnumerator<TelecomServerComponent, TelecomRouterComponent,
            ApcPowerReceiverComponent, TransformComponent>();
        EntityUid? incompleteServer = null;

        while (query.MoveNext(out var uid, out _, out _, out var power, out var transform))
        {
            if (transform.MapID == mapId &&
                power.Powered &&
                ServerHasChannel(uid, channel))
            {
                if (GetChainStatus(uid, mapId, out _) == TelecomSignalStatus.Routed)
                {
                    result = uid;
                    return true;
                }

                incompleteServer ??= uid;
            }
        }

        if (incompleteServer != null)
        {
            result = incompleteServer.Value;
            return true;
        }

        result = default;
        return false;
    }

    private bool TryGetLinkedPoweredProcessorPair(
        EntityUid busUid,
        MapId mapId,
        out EntityUid processorUid)
    {
        processorUid = default;

        if (!TryComp<DeviceLinkSinkComponent>(busUid, out var busSink) ||
            !TryComp<DeviceLinkSourceComponent>(busUid, out var busSource) ||
            !TryComp<ApcPowerReceiverComponent>(busUid, out var busPower) ||
            !busPower.Powered ||
            Transform(busUid).MapID != mapId)
        {
            return false;
        }

        foreach (var candidate in busSink.LinkedSources)
        {
            if (!HasComp<TelecomProcessorComponent>(candidate) ||
                !TryComp<DeviceLinkSourceComponent>(candidate, out var processorSource) ||
                !IsLinked(processorSource, busUid, ProcessorOutputPort, BusInputPort) ||
                !IsLinked(busSource, candidate, BusOutputPort, ProcessorInputPort) ||
                !TryComp<ApcPowerReceiverComponent>(candidate, out var processorPower) ||
                !processorPower.Powered ||
                Transform(candidate).MapID != mapId)
            {
                continue;
            }

            processorUid = candidate;
            return true;
        }

        return false;
    }

    private TelecomSignalStatus GetChainStatus(EntityUid server, MapId mapId, out bool broadcasterSabotaged)
    {
        broadcasterSabotaged = false;

        if (!TryGetLinkedPoweredSource<TelecomBusComponent>(
                server,
                mapId,
                BusOutputPort,
                ServerInputPort,
                out var bus))
            return TelecomSignalStatus.NoBus;

        if (!TryGetLinkedPoweredSource<TelecomReceiverComponent>(
                bus,
                mapId,
                ReceiverOutputPort,
                BusInputPort,
                out _))
            return TelecomSignalStatus.NoReceiver;

        if (!TryGetLinkedPoweredProcessorPair(bus, mapId, out _))
            return TelecomSignalStatus.NoProcessor;

        if (!TryGetLinkedBroadcasterState(server, mapId, out broadcasterSabotaged))
            return TelecomSignalStatus.NoBroadcaster;

        return TelecomSignalStatus.Routed;
    }

    public void RepairRouter(EntityUid server)
    {
        if (TryComp<TelecomRouterComponent>(server, out var router))
        {
            router.Sabotaged = false;
            router.ResetArmed = false;
        }

        if (!TryComp<DeviceLinkSourceComponent>(server, out var source))
            return;

        foreach (var broadcasterUid in source.LinkedPorts.Keys)
        {
            if (IsLinked(source, broadcasterUid, ServerOutputPort, BroadcasterInputPort) &&
                TryComp<TelecomBroadcasterComponent>(broadcasterUid, out var broadcaster))
            {
                broadcaster.Sabotaged = false;
            }
        }
    }

    private void OnRouterEmagged(Entity<TelecomRouterComponent> ent, ref GotEmaggedEvent args)
    {
        if (!_emag.CompareFlag(args.Type, EmagType.Interaction) || ent.Comp.Sabotaged)
            return;

        ent.Comp.Sabotaged = true;
        ent.Comp.ResetArmed = false;
        args.Handled = true;
        args.Repeatable = true;
    }

    private void OnBroadcasterEmagged(Entity<TelecomBroadcasterComponent> ent, ref GotEmaggedEvent args)
    {
        if (!_emag.CompareFlag(args.Type, EmagType.Interaction) || ent.Comp.Sabotaged)
            return;

        ent.Comp.Sabotaged = true;
        args.Handled = true;
        args.Repeatable = true;
    }

    private string GarbleMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return message;

        var chars = message.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsWhiteSpace(chars[i]) && _random.Prob(0.45f))
                chars[i] = _random.Prob(0.5f) ? '~' : '#';
        }

        return new string(chars);
    }

    private bool TryGetLinkedPoweredSource<TComponent>(
        EntityUid sinkUid,
        MapId mapId,
        string sourcePort,
        string sinkPort,
        out EntityUid sourceUid)
        where TComponent : IComponent
    {
        if (!TryComp<DeviceLinkSinkComponent>(sinkUid, out var sink))
        {
            sourceUid = default;
            return false;
        }

        foreach (var candidate in sink.LinkedSources)
        {
            if (!HasComp<TComponent>(candidate) ||
                !TryComp<DeviceLinkSourceComponent>(candidate, out var source) ||
                !IsLinked(source, sinkUid, sourcePort, sinkPort) ||
                !TryComp<ApcPowerReceiverComponent>(candidate, out var power) ||
                !power.Powered ||
                Transform(candidate).MapID != mapId)
            {
                continue;
            }

            sourceUid = candidate;
            return true;
        }

        sourceUid = default;
        return false;
    }

    private bool TryGetLinkedBroadcasterState(EntityUid server, MapId mapId, out bool sabotaged)
    {
        sabotaged = false;
        var powered = false;
        if (!TryComp<DeviceLinkSourceComponent>(server, out var source))
            return false;

        foreach (var broadcasterUid in source.LinkedPorts.Keys)
        {
            if (!IsLinked(source, broadcasterUid, ServerOutputPort, BroadcasterInputPort) ||
                !TryComp<TelecomBroadcasterComponent>(broadcasterUid, out var broadcaster) ||
                !TryComp<ApcPowerReceiverComponent>(broadcasterUid, out var power) ||
                !power.Powered ||
                Transform(broadcasterUid).MapID != mapId)
            {
                continue;
            }

            powered = true;
            sabotaged |= broadcaster.Sabotaged;
        }

        return powered;
    }

    private bool IsBusProcessorLinkedEitherWay(EntityUid busUid, EntityUid processorUid)
    {
        var busToProcessor =
            TryComp<DeviceLinkSourceComponent>(busUid, out var busSource) &&
            IsLinked(busSource, processorUid, BusOutputPort, ProcessorInputPort);

        var processorToBus =
            TryComp<DeviceLinkSourceComponent>(processorUid, out var processorSource) &&
            IsLinked(processorSource, busUid, ProcessorOutputPort, BusInputPort);

        return busToProcessor || processorToBus;
    }

    private int CountLinkedProcessorsForBus(EntityUid busUid)
    {
        var processors = new HashSet<EntityUid>();

        if (TryComp<DeviceLinkSourceComponent>(busUid, out var busSource))
        {
            foreach (var target in busSource.LinkedPorts.Keys)
            {
                if (HasComp<TelecomProcessorComponent>(target) &&
                    IsLinked(busSource, target, BusOutputPort, ProcessorInputPort))
                {
                    processors.Add(target);
                }
            }
        }

        if (TryComp<DeviceLinkSinkComponent>(busUid, out var busSink))
        {
            foreach (var sourceUid in busSink.LinkedSources)
            {
                if (HasComp<TelecomProcessorComponent>(sourceUid) &&
                    TryComp<DeviceLinkSourceComponent>(sourceUid, out var processorSource) &&
                    IsLinked(processorSource, busUid, ProcessorOutputPort, BusInputPort))
                {
                    processors.Add(sourceUid);
                }
            }
        }

        return processors.Count;
    }

    private int CountLinkedBusesForProcessor(EntityUid processorUid)
    {
        var buses = new HashSet<EntityUid>();

        if (TryComp<DeviceLinkSourceComponent>(processorUid, out var processorSource))
        {
            foreach (var target in processorSource.LinkedPorts.Keys)
            {
                if (HasComp<TelecomBusComponent>(target) &&
                    IsLinked(processorSource, target, ProcessorOutputPort, BusInputPort))
                {
                    buses.Add(target);
                }
            }
        }

        if (TryComp<DeviceLinkSinkComponent>(processorUid, out var processorSink))
        {
            foreach (var sourceUid in processorSink.LinkedSources)
            {
                if (HasComp<TelecomBusComponent>(sourceUid) &&
                    TryComp<DeviceLinkSourceComponent>(sourceUid, out var busSource) &&
                    IsLinked(busSource, processorUid, BusOutputPort, ProcessorInputPort))
                {
                    buses.Add(sourceUid);
                }
            }
        }

        return buses.Count;
    }

    private static bool IsLinked(
        DeviceLinkSourceComponent source,
        EntityUid sink,
        string sourcePort,
        string sinkPort)
    {
        foreach (var (linkedSink, links) in source.LinkedPorts)
        {
            if (linkedSink != sink)
                continue;

            foreach (var link in links)
            {
                if (link.Source == sourcePort && link.Sink == sinkPort)
                    return true;
            }
        }

        return false;
    }

    private void OnReceiverLinkAttempt(Entity<TelecomReceiverComponent> ent, ref LinkAttemptEvent args)
    {
        if (args.Source != ent.Owner ||
            args.SourcePort != ReceiverOutputPort ||
            args.SinkPort != BusInputPort)
            return;

        if (!HasComp<TelecomBusComponent>(args.Sink) ||
            CountLinkedTargets<TelecomBusComponent>(ent.Owner, ReceiverOutputPort, BusInputPort) >= ent.Comp.MaxBuses &&
            !IsAlreadyLinked(ent.Owner, args.Sink, ReceiverOutputPort, BusInputPort))
        {
            args.Cancel();
        }
    }

    private void OnProcessorLinkAttempt(Entity<TelecomProcessorComponent> ent, ref LinkAttemptEvent args)
    {
        if (args.Sink == ent.Owner &&
            args.SourcePort == BusOutputPort &&
            args.SinkPort == ProcessorInputPort)
        {
            if (!HasComp<TelecomBusComponent>(args.Source) ||
                CountLinkedBusesForProcessor(ent.Owner) >= ent.Comp.MaxBuses &&
                !IsBusProcessorLinkedEitherWay(args.Source, ent.Owner))
            {
                args.Cancel();
            }

            return;
        }

        if (args.Sink == ent.Owner &&
            args.SinkPort == ProcessorInputPort)
        {
            args.Cancel();
            return;
        }

        if (args.Source == ent.Owner &&
            args.SourcePort == ProcessorOutputPort &&
            args.SinkPort == BusInputPort)
        {
            if (!HasComp<TelecomBusComponent>(args.Sink) ||
                CountLinkedBusesForProcessor(ent.Owner) >= ent.Comp.MaxBuses &&
                !IsBusProcessorLinkedEitherWay(args.Sink, ent.Owner))
            {
                args.Cancel();
            }

            return;
        }

        if (args.Source == ent.Owner &&
            args.SourcePort == ProcessorOutputPort)
        {
            args.Cancel();
        }
    }

    private void OnBusLinkAttempt(Entity<TelecomBusComponent> ent, ref LinkAttemptEvent args)
    {
        if (args.Sink == ent.Owner &&
            args.SourcePort == ReceiverOutputPort &&
            args.SinkPort == BusInputPort)
        {
            if (!HasComp<TelecomReceiverComponent>(args.Source) ||
                CountLinkedSources<TelecomReceiverComponent>(ent.Owner, ReceiverOutputPort, BusInputPort) >= ent.Comp.MaxReceivers &&
                !IsAlreadyLinked(args.Source, ent.Owner, ReceiverOutputPort, BusInputPort))
            {
                args.Cancel();
            }

            return;
        }

        if (args.Sink == ent.Owner &&
            args.SourcePort == ProcessorOutputPort &&
            args.SinkPort == BusInputPort)
        {
            if (!HasComp<TelecomProcessorComponent>(args.Source) ||
                CountLinkedProcessorsForBus(ent.Owner) >= ent.Comp.MaxProcessors &&
                !IsBusProcessorLinkedEitherWay(ent.Owner, args.Source))
            {
                args.Cancel();
            }

            return;
        }

        if (args.Sink == ent.Owner &&
            args.SinkPort == BusInputPort)
        {
            args.Cancel();
            return;
        }

        if (args.Source == ent.Owner &&
            args.SourcePort == BusOutputPort &&
            args.SinkPort == ProcessorInputPort)
        {
            if (!HasComp<TelecomProcessorComponent>(args.Sink) ||
                CountLinkedProcessorsForBus(ent.Owner) >= ent.Comp.MaxProcessors &&
                !IsBusProcessorLinkedEitherWay(ent.Owner, args.Sink))
            {
                args.Cancel();
            }

            return;
        }

        if (args.Source == ent.Owner &&
            args.SourcePort == BusOutputPort &&
            args.SinkPort == ServerInputPort)
        {
            if (!HasComp<TelecomServerComponent>(args.Sink) ||
                CountLinkedTargets<TelecomServerComponent>(ent.Owner, BusOutputPort, ServerInputPort) >= ent.Comp.MaxServers &&
                !IsAlreadyLinked(ent.Owner, args.Sink, BusOutputPort, ServerInputPort))
            {
                args.Cancel();
            }

            return;
        }

        if (args.Source == ent.Owner &&
            args.SourcePort == BusOutputPort)
        {
            args.Cancel();
        }
    }
    private void OnServerLinkAttempt(Entity<TelecomSignalLogComponent> ent, ref LinkAttemptEvent args)
    {
        if (args.Sink == ent.Owner &&
            args.SourcePort == BusOutputPort &&
            args.SinkPort == ServerInputPort)
        {
            if (!HasComp<TelecomBusComponent>(args.Source) ||
                !TryComp<TelecomRouterComponent>(ent.Owner, out var router) ||
                CountLinkedSources<TelecomBusComponent>(ent.Owner, BusOutputPort, ServerInputPort) >= router.MaxBuses &&
                !IsAlreadyLinked(args.Source, ent.Owner, BusOutputPort, ServerInputPort))
            {
                args.Cancel();
            }

            return;
        }

        if (args.Source == ent.Owner &&
            args.SourcePort == ServerOutputPort &&
            args.SinkPort == BroadcasterInputPort &&
            !HasComp<TelecomBroadcasterComponent>(args.Sink))
        {
            args.Cancel();
            return;
        }

        if (args.Source == ent.Owner &&
            args.SourcePort == ServerMonitorOutputPort &&
            args.SinkPort == ConsoleInputPort &&
            !HasComp<TelecomTrafficConsoleComponent>(args.Sink))
        {
            args.Cancel();
        }
    }

    private void OnBroadcasterLinkAttempt(Entity<TelecomBroadcasterComponent> ent, ref LinkAttemptEvent args)
    {
        if (args.Sink != ent.Owner ||
            args.SourcePort != ServerOutputPort ||
            args.SinkPort != BroadcasterInputPort)
            return;

        if (!HasComp<TelecomServerComponent>(args.Source) ||
            CountLinkedSources<TelecomServerComponent>(ent.Owner, ServerOutputPort, BroadcasterInputPort) >= ent.Comp.MaxServers &&
            !IsAlreadyLinked(args.Source, ent.Owner, ServerOutputPort, BroadcasterInputPort))
        {
            args.Cancel();
        }
    }

    private void OnConsoleLinkAttempt(Entity<TelecomTrafficConsoleComponent> ent, ref LinkAttemptEvent args)
    {
        if (args.Sink != ent.Owner ||
            args.SourcePort != ServerMonitorOutputPort ||
            args.SinkPort != ConsoleInputPort)
            return;

        if (!HasComp<TelecomServerComponent>(args.Source))
            args.Cancel();
    }

    private int CountLinkedTargets<TComponent>(EntityUid sourceUid, string sourcePort, string sinkPort)
        where TComponent : IComponent
    {
        if (!TryComp<DeviceLinkSourceComponent>(sourceUid, out var source))
            return 0;

        var count = 0;
        foreach (var target in source.LinkedPorts.Keys)
        {
            if (HasComp<TComponent>(target) &&
                IsLinked(source, target, sourcePort, sinkPort))
            {
                count++;
            }
        }

        return count;
    }

    private int CountLinkedSources<TComponent>(EntityUid sinkUid, string sourcePort, string sinkPort)
        where TComponent : IComponent
    {
        if (!TryComp<DeviceLinkSinkComponent>(sinkUid, out var sink))
            return 0;

        var count = 0;
        foreach (var source in sink.LinkedSources)
        {
            if (HasComp<TComponent>(source) &&
                TryComp<DeviceLinkSourceComponent>(source, out var sourceComp) &&
                IsLinked(sourceComp, sinkUid, sourcePort, sinkPort))
            {
                count++;
            }
        }

        return count;
    }

    private bool IsAlreadyLinked(EntityUid sourceUid, EntityUid sinkUid, string sourcePort, string sinkPort)
    {
        return TryComp<DeviceLinkSourceComponent>(sourceUid, out var source) &&
               IsLinked(source, sinkUid, sourcePort, sinkPort);
    }

    private void AddFallbackLog(
        MapId mapId,
        RadioChannelPrototype channel,
        EntityUid messageSource,
        string message,
        TelecomSignalStatus status,
        int messageLength)
    {
        var query = EntityQueryEnumerator<TelecomServerComponent, TelecomSignalLogComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var log, out var transform))
        {
            if (transform.MapID != mapId)
                continue;

            AddLogEntry(log, channel, messageSource, message, status, messageLength);
            return;
        }
    }

    private void AddLogEntry(
        TelecomSignalLogComponent component,
        RadioChannelPrototype channel,
        EntityUid messageSource,
        string message,
        TelecomSignalStatus status,
        int messageLength)
    {
        component.Entries.Add(new TelecomSignalLogEntry(
            _timing.CurTime,
            channel.LocalizedName,
            Name(messageSource),
            message,
            status,
            messageLength));

        var overflow = component.Entries.Count - Math.Max(1, component.MaxEntries);
        if (overflow > 0)
            component.Entries.RemoveRange(0, overflow);
    }

    private void OnLogExamined(Entity<TelecomSignalLogComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString(
            "telecom-signal-log-count",
            ("count", ent.Comp.Entries.Count),
            ("capacity", ent.Comp.MaxEntries)));

        if (ent.Comp.Entries.Count == 0)
            return;

        var entry = ent.Comp.Entries[^1];
        args.PushMarkup(Loc.GetString(
            "telecom-signal-log-last",
            ("time", FormatTime(entry.Timestamp)),
            ("channel", FormattedMessage.EscapeText(entry.Channel)),
            ("source", FormattedMessage.EscapeText(entry.Source)),
            ("message", FormattedMessage.EscapeText(entry.Message))));
    }

    private static string FormatTime(TimeSpan time)
    {
        return $"{(int) time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
    }
}

public readonly record struct TelecomRouteResult(bool CanBroadcast, string Message);