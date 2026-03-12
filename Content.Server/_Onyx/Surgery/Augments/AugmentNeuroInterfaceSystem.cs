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
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed partial class AugmentNeuroInterfaceSystem : EntitySystem
{
    private enum BrainPenaltyStage : byte
    {
        None = 0,
        Below80 = 1,
        Below60 = 2,
        Below50 = 3,
        Below30 = 4,
        Below20 = 5,
        Below10 = 6,
        Destroyed = 7,
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
    private static readonly TimeSpan BrainOverloadPopupInterval = TimeSpan.FromSeconds(6);
    private const float DefaultNeuroLoadWithoutInterface = 5f;
    private const float BrainPenaltyThreshold80 = 0.80f;
    private const float BrainPenaltyThreshold60 = 0.60f;
    private const float BrainPenaltyThreshold50 = 0.50f;
    private const float BrainPenaltyThreshold30 = 0.30f;
    private const float BrainPenaltyThreshold20 = 0.20f;
    private const float BrainPenaltyThreshold10 = 0.10f;
    private const float BrainNeuroLoadPenalty = 5f;
    private const float BrainSlowdownMultiplier60 = 0.9f;
    private const float BrainSlowdownMultiplier50 = 0.7f;
    private const float CriticalDropChancePerHeldItem30 = 0.15f;
    private const float CriticalDropChancePerHeldItem20 = 0.5f;
    private const float BrainAccentMessageReplaceChance60 = 0.10f;
    private const float BrainAccentLetterSwapChance60 = 0.20f;
    private const float BrainAccentMessageReplaceChance50 = 0.15f;
    private const float BrainAccentLetterSwapChance50 = 0.30f;
    private const string NeuroLoadOverloadIdentifier = "NeuroLoadOverload";
    private const float NeuroOverloadDamagePerSecond = 0.1f;
    private readonly Dictionary<EntityUid, TimeSpan> _nextUiUpdate = new();
    private readonly Dictionary<EntityUid, BrainPenaltyStage> _brainPenaltyStages = new();
    private readonly Dictionary<EntityUid, TimeSpan> _nextBrainOverloadPopup = new();
    private readonly Dictionary<EntityUid, EntityUid> _adminRemoteControlBodies = new();
    private TimeSpan _nextOverloadDamageSweep = TimeSpan.Zero;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<AugmentNeuroInterfaceComponent, ComponentRemove>(OnRemoved);
        SubscribeLocalEvent<AugmentNeuroLoadComponent, CollectAugmentNeuroInterfaceMetricsEvent>(OnCollectNeuroLoadMetrics);
        SubscribeLocalEvent<MovementSpeedModifierComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<BodyComponent, ComponentShutdown>(OnBodyShutdown);
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

    private void OnUiClosed(Entity<AugmentNeuroInterfaceComponent> ent, ref BoundUIClosedEvent args)
    {
        if (!args.Actor.IsValid())
            return;

        if (_augment.GetBody(ent) is not { } body)
            return;

        RemoveRemoteControlIfMatches(args.Actor, body);
    }

    private void OnRemoved(Entity<AugmentNeuroInterfaceComponent> ent, ref ComponentRemove args)
    {
        _nextUiUpdate.Remove(ent.Owner);

        if (_augment.GetBody(ent) is not { } body)
            return;

        RemoveRemoteControllersForBody(body);
    }

    private void RemoveRemoteControlIfMatches(EntityUid actor, EntityUid body)
    {
        if (_adminRemoteControlBodies.TryGetValue(actor, out var controlledBody) && controlledBody == body)
            _adminRemoteControlBodies.Remove(actor);
    }

    private void RemoveRemoteControllersForBody(EntityUid body)
    {
        var toRemove = new List<EntityUid>();
        foreach (var pair in _adminRemoteControlBodies)
        {
            if (pair.Value == body)
                toRemove.Add(pair.Key);
        }

        foreach (var controller in toRemove)
        {
            _adminRemoteControlBodies.Remove(controller);
        }
    }

    private void OnBodyShutdown(Entity<BodyComponent> ent, ref ComponentShutdown args)
    {
        _brainPenaltyStages.Remove(ent.Owner);
        _nextBrainOverloadPopup.Remove(ent.Owner);
    }

    private void OnCollectNeuroLoadMetrics(Entity<AugmentNeuroLoadComponent> ent, ref CollectAugmentNeuroInterfaceMetricsEvent args)
    {
        if (ent.Comp.PassiveLoad <= 0f)
            return;

        args.PassiveNeuroLoadEntries.Add(new NeuroInterfaceMetricEntry("neuro-interface-tooltip-source-neuro-passive", ent.Comp.PassiveLoad));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

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

    public bool TryOpenInterfaceForRemoteController(EntityUid body, EntityUid controller, out string error)
    {
        error = string.Empty;

        if (!TryGetNeuroInterfaceEntity(body, out var neuroEntity))
        {
            error = "No installed neuro-interface found in this body.";
            return false;
        }

        _adminRemoteControlBodies[controller] = body;
        if (!_ui.TryOpenUi(neuroEntity.Owner, NeuroInterfaceUiKey.Key, controller))
        {
            _adminRemoteControlBodies.Remove(controller);
            error = "Failed to open neuro-interface UI.";
            return false;
        }

        UpdateUi(neuroEntity, body);
        return true;
    }

    private bool TryGetNeuroInterfaceEntity(EntityUid body, out Entity<AugmentNeuroInterfaceComponent> neuroEntity)
    {
        foreach (var (partUid, partComp) in _body.GetBodyChildren(body))
        {
            foreach (var (organUid, _) in _body.GetPartOrgans(partUid, partComp))
            {
                if (!TryComp<AugmentNeuroInterfaceComponent>(organUid, out var neuroComp))
                    continue;

                neuroEntity = (organUid, neuroComp);
                return true;
            }
        }

        neuroEntity = default;
        return false;
    }

    private bool HasAdminRemoteControl(EntityUid body, EntityUid actor)
    {
        return _adminRemoteControlBodies.TryGetValue(actor, out var controlledBody) && controlledBody == body;
    }

}
