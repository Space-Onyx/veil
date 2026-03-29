// SPDX-FileCopyrightText: 2021 Alex Evgrashin <aevgrashin@yandex.ru>
// SPDX-FileCopyrightText: 2021 Paul Ritter <ritter.paul1@googlemail.com>
// SPDX-FileCopyrightText: 2021 metalgearsloth <comedian_vs_clown@hotmail.com>
// SPDX-FileCopyrightText: 2022 Leon Friedrich <60421075+ElectroJr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 mirrorcult <lunarautomaton6@gmail.com>
// SPDX-FileCopyrightText: 2023 chromiumboy <50505512+chromiumboy@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
//
// SPDX-License-Identifier: MIT

using Content.Shared.Medical.SuitSensor;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared.Medical.CrewMonitoring;

[Serializable, NetSerializable]
public enum CrewMonitoringUIKey
{
    Key
}

[Serializable, NetSerializable]
public sealed class CrewMonitoringState : BoundUserInterfaceState
{
    public List<SuitSensorStatus> Sensors;
    public List<int> Floors; // <Onyx-ZLevelsTweak>
    public int SelectedFloor; // <Onyx-ZLevelsTweak>
    public int MonitorFloor; // <Onyx-ZLevelsTweak>
    public NetEntity? SelectedFloorMap; // <Onyx-ZLevelsTweak>

    // <Onyx-ZLevelsTweak edited>
    public CrewMonitoringState(
        List<SuitSensorStatus> sensors,
        List<int> floors,
        int selectedFloor,
        int monitorFloor,
        NetEntity? selectedFloorMap)
    {
        Sensors = sensors;
        Floors = floors;
        SelectedFloor = selectedFloor;
        MonitorFloor = monitorFloor;
        SelectedFloorMap = selectedFloorMap;
    }
    // </Onyx-ZLevelsTweak edited>
}

// <Onyx-ZLevelsTweak>
[Serializable, NetSerializable]
public sealed class CrewMonitoringSelectFloorMessage : BoundUserInterfaceMessage
{
    public int Floor;

    public CrewMonitoringSelectFloorMessage(int floor)
    {
        Floor = floor;
    }
}
// </Onyx-ZLevelsTweak>