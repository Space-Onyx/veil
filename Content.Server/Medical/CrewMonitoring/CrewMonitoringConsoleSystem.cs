// SPDX-FileCopyrightText: 2021 Alex Evgrashin <aevgrashin@yandex.ru>
// SPDX-FileCopyrightText: 2021 Paul Ritter <ritter.paul1@googlemail.com>
// SPDX-FileCopyrightText: 2022 Acruid <shatter66@gmail.com>
// SPDX-FileCopyrightText: 2022 Leon Friedrich <60421075+ElectroJr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 metalgearsloth <comedian_vs_clown@hotmail.com>
// SPDX-FileCopyrightText: 2022 mirrorcult <lunarautomaton6@gmail.com>
// SPDX-FileCopyrightText: 2023 Julian Giebel <juliangiebel@live.de>
// SPDX-FileCopyrightText: 2023 TemporalOroboros <TemporalOroboros@gmail.com>
// SPDX-FileCopyrightText: 2023 chromiumboy <50505512+chromiumboy@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 keronshb <54602815+keronshb@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 themias <89101928+themias@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Baptr0b0t <152836416+Baptr0b0t@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 Ted Lukin <66275205+pheenty@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 pheenty <fedorlukin2006@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Goobstation.Shared.CrewMonitoring;
using Content.Server.DeviceNetwork;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.PowerCell;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Events;
using Content.Shared.Medical.CrewMonitoring;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Pinpointer;
using Content.Shared._Onyx.UI;
using Robust.Server.GameObjects;

namespace Content.Server.Medical.CrewMonitoring;

public sealed class CrewMonitoringConsoleSystem : EntitySystem
{
    [Dependency] private readonly PowerCellSystem _cell = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly CESharedZLevelsSystem _zLevels = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CrewMonitoringConsoleComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<CrewMonitoringConsoleComponent, DeviceNetworkPacketEvent>(OnPacketReceived);
        SubscribeLocalEvent<CrewMonitoringConsoleComponent, BoundUIOpenedEvent>(OnUIOpened);
        SubscribeLocalEvent<CrewMonitoringConsoleComponent, CrewMonitoringSelectFloorMessage>(OnSelectFloorMessage);
    }

    private void OnRemove(EntityUid uid, CrewMonitoringConsoleComponent component, ComponentRemove args)
    {
        component.ConnectedSensors.Clear();
    }

    private void OnPacketReceived(EntityUid uid, CrewMonitoringConsoleComponent component, DeviceNetworkPacketEvent args)
    {
        var payload = args.Data;

        // Check command
        if (!payload.TryGetValue(DeviceNetworkConstants.Command, out string? command))
            return;

        if (command != DeviceNetworkConstants.CmdUpdatedState)
            return;

        if (!payload.TryGetValue(SuitSensorConstants.NET_STATUS_COLLECTION, out Dictionary<string, SuitSensorStatus>? sensorStatus))
            return;
        component.ConnectedSensors = sensorStatus;

        UpdateUserInterface(uid, component);
    }

    private void OnUIOpened(EntityUid uid, CrewMonitoringConsoleComponent component, BoundUIOpenedEvent args)
    {
        if (!_cell.TryUseActivatableCharge(uid))
            return;

        ZLevelFloorSelectorHelper.EnsureNavMapsForLinkedFloors(EntityManager, _zLevels, uid);

        var floorState = ZLevelFloorSelectorHelper.GetFloorState(
            EntityManager,
            _zLevels,
            uid,
            component.SelectedFloorDepth);
        component.SelectedFloorDepth = floorState.SelectedFloor;

        UpdateUserInterface(uid, component);
    }

    private void OnSelectFloorMessage(
        EntityUid uid,
        CrewMonitoringConsoleComponent component,
        CrewMonitoringSelectFloorMessage message)
    {
        var floorState = ZLevelFloorSelectorHelper.GetFloorState(
            EntityManager,
            _zLevels,
            uid,
            component.SelectedFloorDepth);
        component.SelectedFloorDepth = floorState.SelectedFloor;

        var floors = floorState.Floors;
        if (floors.Count <= 1 || !floors.Contains(message.Floor))
            return;

        if (floorState.SelectedFloor == message.Floor)
            return;

        component.SelectedFloorDepth = message.Floor;
        UpdateUserInterface(uid, component);
    }

    private void UpdateUserInterface(EntityUid uid, CrewMonitoringConsoleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (!_uiSystem.IsUiOpen(uid, CrewMonitoringUIKey.Key))
            return;

        var floorState = ZLevelFloorSelectorHelper.GetFloorState(
            EntityManager,
            _zLevels,
            uid,
            component.SelectedFloorDepth);
        component.SelectedFloorDepth = floorState.SelectedFloor;

        var targetMapUid = ZLevelFloorSelectorHelper.ResolveMapUid(EntityManager, floorState.SelectedMap);
        var consoleGridUid = Transform(uid).GridUid;

        if (targetMapUid != null)
            EnsureComp<NavMapComponent>(targetMapUid.Value);
        else if (consoleGridUid != null)
            EnsureComp<NavMapComponent>(consoleGridUid.Value);

        // Update all sensors info
        // GoobStation - Start
        var isCommandOnly = HasComp<CrewMonitorScanningComponent>(uid);

        var filteredSensors = component.ConnectedSensors
            .Where(pair => isCommandOnly
                ? pair.Value.IsCommandTracker
                : !pair.Value.IsCommandTracker)
            .Select(pair => pair.Value)
            .Where(sensor => IsSensorOnFloor(sensor, floorState.SelectedFloor))
            .ToList();
        _uiSystem.SetUiState(uid, CrewMonitoringUIKey.Key, new CrewMonitoringState(
            filteredSensors,
            floorState.Floors,
            floorState.SelectedFloor,
            floorState.SourceFloor,
            floorState.SelectedMap is { } selectedMap ? GetNetEntity(selectedMap) : null));
        // GoobStation - End
        //var allSensors = component.ConnectedSensors.Values.ToList();
        //_uiSystem.SetUiState(uid, CrewMonitoringUIKey.Key, new CrewMonitoringState(allSensors));
    }

    private bool IsSensorOnFloor(SuitSensorStatus sensor, int selectedFloor)
    {
        if (sensor.Coordinates == null)
            return true;

        if (ZLevelFloorSelectorHelper.TryGetMapDepthForNetEntity(EntityManager, sensor.OwnerUid, out var depth))
            return depth == selectedFloor;

        var entityCoordinates = EntityManager.GetCoordinates(sensor.Coordinates.Value);
        if (!TryComp<TransformComponent>(entityCoordinates.EntityId, out var xform) || xform.MapUid == null)
            return true;

        if (!TryComp<CEZLevelMapComponent>(xform.MapUid.Value, out var zMap))
            return true;

        return zMap.Depth == selectedFloor;
    }
}
