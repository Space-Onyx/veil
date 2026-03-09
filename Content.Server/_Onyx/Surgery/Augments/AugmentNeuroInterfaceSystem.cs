using Content.Goobstation.Shared.Augments;
using Content.Server.Body.Systems;
using Content.Server.PowerCell;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Body.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Server.Hands.Systems;
using Content.Shared._Shitmed.Medical.Surgery.Traumas.Systems;
using Content.Shared.Containers.ItemSlots;
using Robust.Server.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed partial class AugmentNeuroInterfaceSystem : EntitySystem
{
    private enum BrainPenaltyStage : byte
    {
        None = 0,
        Below75 = 1,
        Below50 = 2,
        Below30 = 3,
    }

    private sealed class AugmentMetricBreakdown
    {
        public List<NeuroInterfaceMetricEntry> PassivePowerEntries = new();
        public List<NeuroInterfaceMetricEntry> ActivePowerEntries = new();
        public List<NeuroInterfaceMetricEntry> PassiveNeuroLoadEntries = new();
        public List<NeuroInterfaceMetricEntry> ActiveNeuroLoadEntries = new();
    }

    [Dependency] private readonly AugmentSystem _augment = default!;
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAugmentPowerCellSystem _augmentPower = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly TraumaSystem _trauma = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    private static readonly TimeSpan UiUpdateInterval = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan OverloadDamageInterval = TimeSpan.FromSeconds(1);
    private const float DefaultNeuroLoadWithoutInterface = 5f;
    private const float BrainPenaltyThreshold75 = 0.75f;
    private const float BrainPenaltyThreshold50 = 0.50f;
    private const float BrainPenaltyThreshold30 = 0.30f;
    private const float BrainNeuroLoadPenalty = 5f;
    private const float BrainSlowdownMultiplier = 0.6f;
    private const float CriticalDropChancePerHeldItem = 0.2f;
    private const string NeuroLoadOverloadIdentifier = "NeuroLoadOverload";
    private const float NeuroOverloadDamagePerSecond = 0.1f;
    private readonly Dictionary<EntityUid, TimeSpan> _nextUiUpdate = new();
    private readonly Dictionary<EntityUid, BrainPenaltyStage> _brainPenaltyStages = new();
    private TimeSpan _nextOverloadDamageSweep = TimeSpan.Zero;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, ComponentRemove>(OnRemoved);
        SubscribeLocalEvent<AugmentNeuroLoadComponent, CollectAugmentNeuroInterfaceMetricsEvent>(OnCollectNeuroLoadMetrics);
        SubscribeLocalEvent<MovementSpeedModifierComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<BodyComponent, ComponentShutdown>(OnBodyShutdown);
        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, BoundUserInterfaceCheckRangeEvent>(OnNeuroInterfaceRangeCheck);
        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, GetAugmentsPowerDrawEvent>(OnGetRemoteManipulationPenaltyPowerDraw);

        Subs.BuiEvents<AugmentNeuroInterfaceComponent>(NeuroInterfaceUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<BoundUIClosedEvent>(OnUiClosed);
            subs.Event<NeuroInterfaceToggleAugmentMessage>(OnToggleAugment);
            subs.Event<NeuroInterfaceBulkToggleMessage>(OnBulkToggle);
        });
    }

    private void OnInit(Entity<AugmentNeuroInterfaceComponent> ent, ref ComponentInit args)
    {
        if (string.IsNullOrWhiteSpace(ent.Comp.InterfaceCode))
            ent.Comp.InterfaceCode = GenerateHexCode();
    }

    private void OnUiOpened(Entity<AugmentNeuroInterfaceComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (_augment.GetBody(ent) is not { } body)
            return;

        UpdateUi(ent, body);
        _nextUiUpdate[ent.Owner] = _timing.CurTime + UiUpdateInterval;
    }

    private void OnRemoved(Entity<AugmentNeuroInterfaceComponent> ent, ref ComponentRemove args)
    {
        _nextUiUpdate.Remove(ent.Owner);
        ent.Comp.AuthorizedRemoteViewers.Clear();
    }

    private void OnUiClosed(Entity<AugmentNeuroInterfaceComponent> ent, ref BoundUIClosedEvent args)
    {
        if (args.UiKey is not NeuroInterfaceUiKey.Key)
            return;

        if (!args.Actor.Valid)
            return;

        if (_augment.GetBody(ent) is { } body && args.Actor == body)
            return;

        ent.Comp.AuthorizedRemoteViewers.Remove(args.Actor);
    }

    private void OnBodyShutdown(Entity<BodyComponent> ent, ref ComponentShutdown args)
    {
        _brainPenaltyStages.Remove(ent.Owner);
        _remoteManipulationPenalties.Remove(ent.Owner);
    }

    private void OnCollectNeuroLoadMetrics(Entity<AugmentNeuroLoadComponent> ent, ref CollectAugmentNeuroInterfaceMetricsEvent args)
    {
        if (ent.Comp.PassiveLoad <= 0f)
            return;

        args.PassiveNeuroLoadEntries.Add(new NeuroInterfaceMetricEntry("neuro-interface-tooltip-source-neuro-passive", ent.Comp.PassiveLoad));
    }

    private void OnNeuroInterfaceRangeCheck(Entity<AugmentNeuroInterfaceComponent> ent, ref BoundUserInterfaceCheckRangeEvent args)
    {
        if (args.UiKey is not NeuroInterfaceUiKey.Key)
            return;

        if (_augment.GetBody(ent) is not { } body)
            return;

        if (args.Actor.Owner == body)
            return;

        if (ent.Comp.AuthorizedRemoteViewers.Contains(args.Actor.Owner))
            args.Result = BoundUserInterfaceRangeResult.Pass;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        PruneRemoteManipulationPenalties(now);

        if (now >= _nextOverloadDamageSweep)
        {
            _nextOverloadDamageSweep = now + OverloadDamageInterval;
            var bodyQuery = EntityQueryEnumerator<BodyComponent>();
            while (bodyQuery.MoveNext(out var bodyUid, out _))
            {
                ApplyBrainIntegrityEffects(bodyUid);
                ApplyOverloadDamage(bodyUid);
            }
        }

        var query = EntityQueryEnumerator<AugmentNeuroInterfaceComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_augment.GetBody(uid) is not { } body)
                continue;

            if (!_ui.IsUiOpen(uid, NeuroInterfaceUiKey.Key))
                continue;

            if (_nextUiUpdate.TryGetValue(uid, out var next) && next > now)
                continue;

            _nextUiUpdate[uid] = now + UiUpdateInterval;
            UpdateUi((uid, comp), body);
        }
    }

}
