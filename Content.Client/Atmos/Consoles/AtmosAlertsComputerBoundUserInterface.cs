// SPDX-FileCopyrightText: 2024 MilenVolf <63782763+MilenVolf@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Piras314 <p1r4s@proton.me>
// SPDX-FileCopyrightText: 2024 chromiumboy <50505512+chromiumboy@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Milon <milonpl.git@proton.me>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Atmos.Components;

namespace Content.Client.Atmos.Consoles;

public sealed class AtmosAlertsComputerBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private AtmosAlertsComputerWindow? _menu;

    public AtmosAlertsComputerBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();

        _menu = new AtmosAlertsComputerWindow(this, Owner);
        _menu.OpenCentered();
        _menu.OnClose += Close;
        _menu.FloorSelected += SendSelectFloorMessage; // <Onyx-ZLevelsTweak>
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        var castState = (AtmosAlertsComputerBoundInterfaceState) state;

        EntMan.TryGetComponent<TransformComponent>(Owner, out var xform);
        // <Onyx-ZLevelsTweak edited>
        _menu?.UpdateUI(
            xform?.Coordinates,
            castState.AirAlarms,
            castState.FireAlarms,
            castState.FocusData,
            castState.Floors,
            castState.SelectedFloor,
            castState.MonitorFloor,
            castState.SelectedFloorMap);
        // </Onyx-ZLevelsTweak edited>
    }

    public void SendFocusChangeMessage(NetEntity? netEntity)
    {
        SendMessage(new AtmosAlertsComputerFocusChangeMessage(netEntity));
    }

    public void SendDeviceSilencedMessage(NetEntity netEntity, bool silenceDevice)
    {
        SendMessage(new AtmosAlertsComputerDeviceSilencedMessage(netEntity, silenceDevice));
    }

    // <Onyx-ZLevelsTweak>
    public void SendSelectFloorMessage(int floor)
    {
        SendMessage(new AtmosAlertsComputerSelectFloorMessage(floor));
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