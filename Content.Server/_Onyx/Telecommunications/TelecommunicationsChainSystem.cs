using Content.Server.Power.Components;
using Content.Server.Popups;
using Content.Server.Emp;
using Content.Server._Onyx.Telecommunications.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Emp;
using Content.Shared._Onyx.Telecommunications;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Emag.Systems;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Tools.Systems;
using Content.Shared.Wires;
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
    [Dependency] private readonly SharedToolSystem _tools = default!;
    [Dependency] private readonly PopupSystem _popup = default!;

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
        SubscribeLocalEvent<TelecomCalibrationComponent, InteractUsingEvent>(OnCalibrationInteract);
        SubscribeLocalEvent<TelecomCalibrationComponent, TelecomCalibrationFinishedEvent>(OnCalibrationFinished);
        SubscribeLocalEvent<TelecomCalibrationComponent, DamageChangedEvent>(OnCalibrationDamaged);
        SubscribeLocalEvent<TelecomCalibrationComponent, EmpPulseEvent>(OnCalibrationEmp);
        SubscribeLocalEvent<TelecomCalibrationComponent, ExaminedEvent>(OnCalibrationExamined);
        SubscribeLocalEvent<TelecomSolarFlareEvent>(OnSolarFlare);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<TelecomCalibrationComponent>();
        while (query.MoveNext(out _, out var calibration))
        {
            calibration.Calibration = Math.Clamp(
                calibration.Calibration - calibration.DecayPerHour / 3600f * frameTime,
                0f,
                100f);

            calibration.CurrentLoad = Math.Max(
                0f,
                calibration.CurrentLoad - Math.Max(0.1f, calibration.Bandwidth) * frameTime);

            calibration.TelemetryLoad = Math.Max(
                0f,
                calibration.TelemetryLoad - Math.Max(0.1f, calibration.Bandwidth) / 10f * frameTime);
        }
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
        var chainStatus = GetChainStatus(server, mapId, out var chain);
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

        var packetCost = 1f + message.Length / 80f;
        var metrics = ApplyTrafficLoad(chain, packetCost, out var broadcasterSabotaged);
        var calibrationLoss = 1f - metrics.Quality;
        var calibrationDropChance = calibrationLoss * calibrationLoss * 0.65f;
        var congestionDropChance = Math.Clamp((metrics.MaxUtilization - 1f) * 0.35f, 0f, 0.65f);
        var totalDropChance = Math.Clamp(calibrationDropChance + congestionDropChance, 0f, 0.8f);

        if (_random.Prob(totalDropChance))
        {
            var status = congestionDropChance >= calibrationDropChance
                ? TelecomSignalStatus.Congested
                : TelecomSignalStatus.SignalLoss;
            AddLogEntry(log, channel, messageSource, message, status, message.Length, metrics);
            return new TelecomRouteResult(false, message);
        }

        var garbleChance = Math.Clamp(
            Math.Max(0f, 0.95f - metrics.Quality) * 0.25f +
            Math.Max(0f, metrics.MaxUtilization - 0.75f) * 0.12f,
            0f,
            0.7f);

        if (router.Sabotaged || broadcasterSabotaged || _random.Prob(garbleChance))
        {
            var garbled = GarbleMessage(message);
            AddLogEntry(log, channel, messageSource, garbled,
                TelecomSignalStatus.Garbled, garbled.Length, metrics);
            return new TelecomRouteResult(true, garbled);
        }

        var routedStatus = metrics.Quality < 0.9f || metrics.MaxUtilization > 0.75f
            ? TelecomSignalStatus.Degraded
            : TelecomSignalStatus.Routed;
        AddLogEntry(log, channel, messageSource, message,
            routedStatus, message.Length, metrics);
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

    private List<EntityUid> GetLinkedPoweredProcessors(EntityUid busUid, MapId mapId)
    {
        var processors = new List<EntityUid>();

        if (!TryComp<DeviceLinkSinkComponent>(busUid, out var busSink) ||
            !TryComp<DeviceLinkSourceComponent>(busUid, out var busSource) ||
            !TryComp<ApcPowerReceiverComponent>(busUid, out var busPower) ||
            !busPower.Powered ||
            Transform(busUid).MapID != mapId)
        {
            return processors;
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

            processors.Add(candidate);
        }

        return processors;
    }

    private TelecomSignalStatus GetChainStatus(EntityUid server, MapId mapId, out TelecomChainSnapshot chain)
    {
        chain = default;

        if (TryComp<TelecomRouterComponent>(server, out var router) && router.Standalone)
        {
            chain = new TelecomChainSnapshot([], [], server, server, [], true);
            return TelecomSignalStatus.Routed;
        }

        if (!TryGetLinkedPoweredSource<TelecomBusComponent>(
                server,
                mapId,
                BusOutputPort,
                ServerInputPort,
                out var bus))
            return TelecomSignalStatus.NoBus;

        var receivers = GetLinkedPoweredSources<TelecomReceiverComponent>(
            bus,
            mapId,
            ReceiverOutputPort,
            BusInputPort);
        if (receivers.Count == 0)
            return TelecomSignalStatus.NoReceiver;

        var processors = GetLinkedPoweredProcessors(bus, mapId);
        if (processors.Count == 0)
            return TelecomSignalStatus.NoProcessor;

        var broadcasters = GetLinkedPoweredBroadcasters(server, mapId);
        if (broadcasters.Count == 0)
            return TelecomSignalStatus.NoBroadcaster;

        chain = new TelecomChainSnapshot(
            receivers,
            processors,
            bus,
            server,
            broadcasters);
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

    private List<EntityUid> GetLinkedPoweredSources<TComponent>(
        EntityUid sinkUid,
        MapId mapId,
        string sourcePort,
        string sinkPort)
        where TComponent : IComponent
    {
        var result = new List<EntityUid>();
        if (!TryComp<DeviceLinkSinkComponent>(sinkUid, out var sink))
            return result;

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

            result.Add(candidate);
        }

        return result;
    }

    private List<EntityUid> GetLinkedPoweredBroadcasters(EntityUid server, MapId mapId)
    {
        var result = new List<EntityUid>();
        if (!TryComp<DeviceLinkSourceComponent>(server, out var source))
            return result;

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

            result.Add(broadcasterUid);
        }

        return result;
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
        int messageLength,
        TelecomTrafficMetrics? metrics = null)
    {
        var traffic = metrics ?? TelecomTrafficMetrics.Empty;
        var storedMessage = status is TelecomSignalStatus.Routed
            or TelecomSignalStatus.Degraded
            or TelecomSignalStatus.Garbled
            ? message
            : string.Empty;
        component.Entries.Add(new TelecomSignalLogEntry(
            _timing.CurTime,
            channel.LocalizedName,
            storedMessage,
            status,
            messageLength,
            (int) MathF.Round(traffic.Quality * 100f),
            (int) MathF.Round(traffic.MaxUtilization * 100f),
            traffic.LatencyMilliseconds));

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
            ("message", string.IsNullOrEmpty(entry.Message)
                ? Loc.GetString("telecom-signal-log-lost")
                : FormattedMessage.EscapeText(entry.Message))));
    }

    private static string FormatTime(TimeSpan time)
    {
        return $"{(int) time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
    }

    public TelecomTrafficMetrics GetServerMetrics(EntityUid server)
    {
        var mapId = Transform(server).MapID;
        return GetChainStatus(server, mapId, out var chain) == TelecomSignalStatus.Routed
            ? ReadTrafficMetrics(chain)
            : TelecomTrafficMetrics.Empty;
    }

    private TelecomTrafficMetrics ApplyTrafficLoad(
        TelecomChainSnapshot chain,
        float packetCost,
        out bool broadcasterSabotaged)
    {
        var receiver = chain.Standalone ? chain.Server : GetLeastLoadedNode(chain.Receivers);
        var processor = chain.Standalone ? chain.Server : GetLeastLoadedNode(chain.Processors);
        var broadcaster = chain.Standalone ? chain.Server : GetLeastLoadedNode(chain.Broadcasters);
        var route = chain.Standalone
            ? new[] { chain.Server }
            : new[] { receiver, processor, chain.Bus, chain.Server, broadcaster };

        foreach (var uid in route)
        {
            if (TryComp<TelecomCalibrationComponent>(uid, out var calibration))
            {
                calibration.CurrentLoad += packetCost;
                calibration.TelemetryLoad = Math.Max(calibration.TelemetryLoad, calibration.CurrentLoad);
            }
        }

        broadcasterSabotaged = !chain.Standalone &&
            TryComp<TelecomBroadcasterComponent>(broadcaster, out var broadcasterComponent) &&
            broadcasterComponent.Sabotaged;
        return ReadPathMetrics(route);
    }

    private TelecomTrafficMetrics ReadTrafficMetrics(TelecomChainSnapshot chain)
    {
        if (chain.Standalone)
        {
            var standaloneMetrics = ReadPoolMetrics([chain.Server], true);
            return BuildTrafficMetrics(standaloneMetrics.Quality, standaloneMetrics.Utilization);
        }

        var receiverMetrics = ReadPoolMetrics(chain.Receivers, true);
        var processorMetrics = ReadPoolMetrics(chain.Processors, true);
        var broadcasterMetrics = ReadPoolMetrics(chain.Broadcasters, true);
        var busMetrics = ReadPoolMetrics([chain.Bus], true);
        var serverMetrics = ReadPoolMetrics([chain.Server], true);

        var quality = (
            receiverMetrics.Quality +
            processorMetrics.Quality +
            busMetrics.Quality +
            serverMetrics.Quality +
            broadcasterMetrics.Quality) / 5f;
        var maxUtilization = Math.Max(
            Math.Max(receiverMetrics.Utilization, processorMetrics.Utilization),
            Math.Max(
                Math.Max(busMetrics.Utilization, serverMetrics.Utilization),
                broadcasterMetrics.Utilization));
        return BuildTrafficMetrics(quality, maxUtilization);
    }

    private TelecomTrafficMetrics ReadPathMetrics(IEnumerable<EntityUid> route)
    {
        var qualityTotal = 0f;
        var qualityCount = 0;
        var maxUtilization = 0f;

        foreach (var uid in route)
        {
            if (!TryComp<TelecomCalibrationComponent>(uid, out var calibration))
                continue;

            qualityTotal += Math.Clamp(calibration.Calibration / 100f, 0f, 1f);
            qualityCount++;
            maxUtilization = Math.Max(
                maxUtilization,
                calibration.CurrentLoad / Math.Max(0.1f, calibration.Bandwidth));
        }

        var quality = qualityCount == 0 ? 1f : qualityTotal / qualityCount;
        return BuildTrafficMetrics(quality, maxUtilization);
    }

    private TelecomPoolMetrics ReadPoolMetrics(
        IEnumerable<EntityUid> nodes,
        bool useTelemetryLoad = false)
    {
        var weightedQuality = 0f;
        var totalBandwidth = 0f;
        var totalLoad = 0f;

        foreach (var uid in nodes)
        {
            if (!TryComp<TelecomCalibrationComponent>(uid, out var calibration))
                continue;

            var bandwidth = Math.Max(0.1f, calibration.Bandwidth);
            weightedQuality += Math.Clamp(calibration.Calibration / 100f, 0f, 1f) * bandwidth;
            totalBandwidth += bandwidth;
            totalLoad += useTelemetryLoad
                ? calibration.TelemetryLoad
                : calibration.CurrentLoad;
        }

        return totalBandwidth <= 0f
            ? new TelecomPoolMetrics(1f, 0f)
            : new TelecomPoolMetrics(weightedQuality / totalBandwidth, totalLoad / totalBandwidth);
    }

    private TelecomTrafficMetrics BuildTrafficMetrics(float quality, float maxUtilization)
    {
        var latency = (int) MathF.Round(
            40f +
            (1f - quality) * 800f +
            Math.Max(0f, maxUtilization - 0.5f) * 1200f);
        return new TelecomTrafficMetrics(quality, maxUtilization, latency);
    }

    private EntityUid GetLeastLoadedNode(IReadOnlyList<EntityUid> nodes)
    {
        var selected = nodes[0];
        var selectedUtilization = GetNodeUtilization(selected);

        for (var i = 1; i < nodes.Count; i++)
        {
            var utilization = GetNodeUtilization(nodes[i]);
            if (utilization >= selectedUtilization)
                continue;

            selected = nodes[i];
            selectedUtilization = utilization;
        }

        return selected;
    }

    private float GetNodeUtilization(EntityUid uid)
    {
        return TryComp<TelecomCalibrationComponent>(uid, out var calibration)
            ? calibration.CurrentLoad / Math.Max(0.1f, calibration.Bandwidth)
            : 0f;
    }

    private void OnCalibrationInteract(Entity<TelecomCalibrationComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_tools.HasQuality(args.Used, SharedToolSystem.PulseQuality))
            return;

        if (TryComp<WiresPanelComponent>(ent.Owner, out var panel) && !panel.Open)
        {
            _popup.PopupEntity(
                Loc.GetString("telecom-calibration-panel-closed"),
                ent.Owner,
                args.User,
                PopupType.Small);
            return;
        }

        args.Handled = _tools.UseTool(
            args.Used,
            args.User,
            ent.Owner,
            6f,
            SharedToolSystem.PulseQuality,
            new TelecomCalibrationFinishedEvent());
    }

    private void OnCalibrationFinished(
        Entity<TelecomCalibrationComponent> ent,
        ref TelecomCalibrationFinishedEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        ent.Comp.Calibration = 100f;
        ent.Comp.CurrentLoad *= 0.5f;
        ent.Comp.TelemetryLoad *= 0.5f;
        args.Handled = true;
        _popup.PopupEntity(
            Loc.GetString("telecom-calibration-complete"),
            ent.Owner,
            args.User,
            PopupType.Medium);
    }

    private void OnCalibrationDamaged(
        Entity<TelecomCalibrationComponent> ent,
        ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null)
            return;

        ent.Comp.Calibration = Math.Max(
            0f,
            ent.Comp.Calibration -
            args.DamageDelta.GetTotal().Float() * ent.Comp.DamageLossMultiplier);
    }

    private void OnCalibrationEmp(Entity<TelecomCalibrationComponent> ent, ref EmpPulseEvent args)
    {
        var loss = Math.Clamp(args.EnergyConsumption / 1000f * 15f, 5f, 25f);
        ent.Comp.Calibration = Math.Max(0f, ent.Comp.Calibration - loss);
        args.Affected = true;
    }

    private void OnCalibrationExamined(Entity<TelecomCalibrationComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var status = ent.Comp.Calibration switch
        {
            >= 90f => "nominal",
            >= 70f => "drifting",
            >= 40f => "poor",
            _ => "critical",
        };

        args.PushMarkup(Loc.GetString(
            $"telecom-calibration-examine-{status}",
            ("calibration", (int) MathF.Round(ent.Comp.Calibration))));
    }

    private void OnSolarFlare(ref TelecomSolarFlareEvent args)
    {
        var query = EntityQueryEnumerator<TelecomCalibrationComponent>();
        while (query.MoveNext(out _, out var calibration))
        {
            var variance = _random.NextFloat(0.8f, 1.2f);
            calibration.Calibration = Math.Max(
                0f,
                calibration.Calibration - args.CalibrationLoss * variance);
        }
    }
}

public readonly record struct TelecomRouteResult(bool CanBroadcast, string Message);

public readonly record struct TelecomTrafficMetrics(
    float Quality,
    float MaxUtilization,
    int LatencyMilliseconds)
{
    public static readonly TelecomTrafficMetrics Empty = new(0f, 0f, 0);
}

internal readonly record struct TelecomChainSnapshot(
    List<EntityUid> Receivers,
    List<EntityUid> Processors,
    EntityUid Bus,
    EntityUid Server,
    List<EntityUid> Broadcasters,
    bool Standalone = false);

internal readonly record struct TelecomPoolMetrics(float Quality, float Utilization);
