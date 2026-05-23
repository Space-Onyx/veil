// SPDX-FileCopyrightText: 2023 Morb <14136326+Morb0@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Kot <1192090+koteq@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
//
// SPDX-License-Identifier: MIT

using Content.Shared.Bed.Sleep;
using Content.Shared._Onyx.ProxyControl;
using Content.Shared.CCVar;
using Content.Shared.NPC; // CorvaxGoob
using Content.Shared.StatusEffectNew;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.SSDIndicator;

/// <summary>
///     Handle changing player SSD indicator status
/// </summary>
public sealed class SSDIndicatorSystem : EntitySystem
{
    public static readonly EntProtoId StatusEffectSSDSleeping = "StatusEffectSSDSleeping";

    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedProxyControlSystem _proxyControl = default!;
    [Dependency] private readonly StatusEffectsSystem _statusEffects = default!;

    private bool _icSsdSleep;
    private float _icSsdSleepTime;

    public override void Initialize()
    {
        SubscribeLocalEvent<SSDIndicatorComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<SSDIndicatorComponent, PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<SSDIndicatorComponent, MapInitEvent>(OnMapInit);

        _cfg.OnValueChanged(CCVars.ICSSDSleep, obj => _icSsdSleep = obj, true);
        _cfg.OnValueChanged(CCVars.ICSSDSleepTime, obj => _icSsdSleepTime = obj, true);
    }

    private void OnPlayerAttached(EntityUid uid, SSDIndicatorComponent component, PlayerAttachedEvent args)
    {
        SetSsdState(uid, component, false);
    }

    private void OnPlayerDetached(EntityUid uid, SSDIndicatorComponent component, PlayerDetachedEvent args)
    {
        SetSsdState(uid, component, !HasActiveProxyController(uid));
    }

    // Prevents mapped mobs to go to sleep immediately
    private void OnMapInit(EntityUid uid, SSDIndicatorComponent component, MapInitEvent args)
    {
        if (HasActiveProxyController(uid))
        {
            SetSsdState(uid, component, false);
            return;
        }

        if (!_icSsdSleep || !component.IsSSD)
            return;

        component.FallAsleepTime = _timing.CurTime + TimeSpan.FromSeconds(_icSsdSleepTime);
        component.NextUpdate = _timing.CurTime + component.UpdateInterval;
        Dirty(uid, component);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_icSsdSleep)
            return;

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<SSDIndicatorComponent>();

        while (query.MoveNext(out var uid, out var ssd))
        {
            if (HasActiveProxyController(uid))
            {
                if (ssd.IsSSD || ssd.FallAsleepTime != TimeSpan.Zero)
                    SetSsdState(uid, ssd, false);

                continue;
            }

            // Forces the entity to sleep when the time has come
            if (!ssd.IsSSD
                || ssd.NextUpdate > curTime
                || ssd.FallAsleepTime > curTime
                || TerminatingOrDeleted(uid)
                || HasComp<ActiveNPCComponent>(uid)) // CorvaxGoob
                continue;

            _statusEffects.TryUpdateStatusEffectDuration(uid, StatusEffectSSDSleeping);
            ssd.NextUpdate += ssd.UpdateInterval;
            Dirty(uid, ssd);
        }
    }

    public void RefreshProxySsdState(EntityUid uid, SSDIndicatorComponent? component = null)
    {
        if (!Resolve(uid, ref component, false) || TerminatingOrDeleted(uid))
            return;

        if (HasActiveProxyController(uid))
        {
            SetSsdState(uid, component, false);
            return;
        }

        if (!HasComp<ActorComponent>(uid))
            SetSsdState(uid, component, true);
    }

    private void SetSsdState(EntityUid uid, SSDIndicatorComponent component, bool isSsd)
    {
        component.IsSSD = isSsd;

        if (_icSsdSleep)
        {
            if (isSsd)
            {
                // Sets the time when the entity should fall asleep
                component.FallAsleepTime = _timing.CurTime + TimeSpan.FromSeconds(_icSsdSleepTime);
            }
            else
            {
                // Removes force sleep and resets the time to zero
                component.FallAsleepTime = TimeSpan.Zero;
                _statusEffects.TryRemoveStatusEffect(uid, StatusEffectSSDSleeping);
            }
        }

        Dirty(uid, component);
    }

    private bool HasActiveProxyController(EntityUid uid, ProxyControlTargetComponent? target = null)
    {
        if (!Resolve(uid, ref target, false))
            return false;

        foreach (var controller in target.Controllers)
        {
            if (TerminatingOrDeleted(controller) ||
                !HasComp<ActorComponent>(controller) ||
                !_proxyControl.IsControllerFor(controller, uid, ProxyControlRelayFlags.None))
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
