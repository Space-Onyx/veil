// SPDX-FileCopyrightText: 2024 chromiumboy <50505512+chromiumboy@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Atmos.Components;

namespace Content.Client.Atmos.Consoles;

public sealed class AtmosMonitoringConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private AtmosMonitoringConsoleWindow? _menu;

    public AtmosMonitoringConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();

        _menu = new AtmosMonitoringConsoleWindow(this, Owner);
        _menu.OpenCentered();
        _menu.OnClose += Close;
        _menu.FloorSelected += SendSelectFloorMessage; // <Onyx-ZLevelsTweak>
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not AtmosMonitoringConsoleBoundInterfaceState castState)
            return;

        EntMan.TryGetComponent<TransformComponent>(Owner, out var xform);
        _menu?.UpdateUI(
            xform?.Coordinates,
            castState.AtmosNetworks,
            castState.Floors,
            castState.SelectedFloor,
            castState.MonitorFloor,
            castState.SelectedFloorMap); // <Onyx-ZLevelsTweak edited>
    }

    // <Onyx-ZLevelsTweak>
    public void SendSelectFloorMessage(int floor)
    {
        SendMessage(new AtmosMonitoringConsoleSelectFloorMessage(floor));
    }
    // </Onyx-ZLevelsTweak>

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Dispose();
    }
}