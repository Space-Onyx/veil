// SPDX-FileCopyrightText: 2026 CorvaxGoob Contributors
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.DeviceLinking.Systems;
using Content.Server.Medical;
using Content.Server.Medical.Components;
using Content.Server._Onyx.Medical.Components;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared.Bed.Components;
using Content.Shared.Body.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.UserInterface;
using System.Linq;

namespace Content.Server._Onyx.Medical;

public sealed class BodyScannerConsoleSystem : EntitySystem
{
    [Dependency] private readonly DeviceLinkSystem _signal = default!;
    [Dependency] private readonly HealthAnalyzerSystem _healthAnalyzer = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyScannerConsoleComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<BodyScannerConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<BodyScannerConsoleComponent, NewLinkEvent>(OnNewLink);
        SubscribeLocalEvent<BodyScannerConsoleComponent, PortDisconnectedEvent>(OnPortDisconnected);
        SubscribeLocalEvent<BodyScannerConsoleComponent, AfterActivatableUIOpenEvent>(OnUiOpen);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<BodyScannerConsoleComponent, HealthAnalyzerComponent>();
        while (query.MoveNext(out var uid, out var console, out var analyzer))
        {
            SyncScanTarget(uid, console, analyzer);
        }
    }

    private void OnInit(Entity<BodyScannerConsoleComponent> ent, ref ComponentInit args)
    {
        _signal.EnsureSinkPorts(ent.Owner, ent.Comp.OperatingTablePort);
    }

    private void OnMapInit(Entity<BodyScannerConsoleComponent> ent, ref MapInitEvent args)
    {
        var linked = ResolveLinkedSource(ent.Owner);
        ent.Comp.LinkedSource = linked;

        if (linked is { } keepSource)
            EnforceSingleSourceLink(ent.Owner, keepSource);
    }

    private void OnNewLink(Entity<BodyScannerConsoleComponent> ent, ref NewLinkEvent args)
    {
        if (args.SinkPort != ent.Comp.OperatingTablePort || !IsSupportedSource(args.Source))
            return;

        EnforceSingleSourceLink(ent.Owner, args.Source);
        ent.Comp.LinkedSource = args.Source;

        if (TryComp<HealthAnalyzerComponent>(ent.Owner, out var analyzer))
            SyncScanTarget(ent.Owner, ent.Comp, analyzer);
    }

    private void OnPortDisconnected(Entity<BodyScannerConsoleComponent> ent, ref PortDisconnectedEvent args)
    {
        if (args.Port != ent.Comp.OperatingTablePort)
            return;

        ent.Comp.LinkedSource = ResolveLinkedSource(ent.Owner);

        if (TryComp<HealthAnalyzerComponent>(ent.Owner, out var analyzer))
            SyncScanTarget(ent.Owner, ent.Comp, analyzer);
    }

    private void OnUiOpen(Entity<BodyScannerConsoleComponent> ent, ref AfterActivatableUIOpenEvent args)
    {
        if (TryComp<HealthAnalyzerComponent>(ent.Owner, out var analyzer))
            SyncScanTarget(ent.Owner, ent.Comp, analyzer);
    }

    private void SyncScanTarget(EntityUid uid, BodyScannerConsoleComponent console, HealthAnalyzerComponent analyzer)
    {
        var nextTarget = GetPatientOnSource(console.LinkedSource);
        var prevTarget = analyzer.ScannedEntity;

        if (prevTarget == nextTarget)
            return;

        if (prevTarget is { } prev && !Deleted(prev))
            _healthAnalyzer.UpdateScannedUser(uid, prev, false, analyzer.CurrentMode);

        analyzer.ScannedEntity = nextTarget;
        analyzer.CurrentBodyPart = null;

        if (nextTarget is { } target)
            _healthAnalyzer.UpdateScannedUser(uid, target, true, analyzer.CurrentMode);
    }

    private EntityUid? GetPatientOnSource(EntityUid? source)
    {
        if (source is not { } sourceUid || Deleted(sourceUid))
            return null;

        if (!TryComp<StrapComponent>(sourceUid, out var strap))
            return null;

        foreach (var buckled in strap.BuckledEntities)
        {
            if (!Deleted(buckled) && HasComp<BodyComponent>(buckled))
                return buckled;
        }

        return null;
    }

    private EntityUid? ResolveLinkedSource(EntityUid uid)
    {
        if (!TryComp<DeviceLinkSinkComponent>(uid, out var sink))
            return null;

        foreach (var source in sink.LinkedSources)
        {
            if (!Deleted(source) && IsSupportedSource(source))
                return source;
        }

        return null;
    }

    private bool IsSupportedSource(EntityUid uid)
    {
        return HasComp<OperatingTableComponent>(uid) || HasComp<StasisBedComponent>(uid);
    }

    private void EnforceSingleSourceLink(EntityUid consoleUid, EntityUid keepSource)
    {
        if (!TryComp<DeviceLinkSinkComponent>(consoleUid, out var sink))
            return;

        foreach (var source in sink.LinkedSources.ToArray())
        {
            if (source == keepSource)
                continue;

            _signal.RemoveSinkFromSource(source, consoleUid);
        }
    }
}
