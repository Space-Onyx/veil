// SPDX-FileCopyrightText: 2022 Flipp Syder <76629141+vulppine@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 Leon Friedrich <60421075+ElectroJr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 TemporalOroboros <TemporalOroboros@gmail.com>
// SPDX-FileCopyrightText: 2024 Nemanja <98561806+EmoGarbage404@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Piras314 <p1r4s@proton.me>
// SPDX-FileCopyrightText: 2024 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client.Eye;
using Content.Shared.SurveillanceCamera;
using Robust.Client.UserInterface;

namespace Content.Client.SurveillanceCamera.UI;

public sealed class SurveillanceCameraMonitorBoundUserInterface : BoundUserInterface
{
    private readonly EyeLerpingSystem _eyeLerpingSystem;
    private readonly SurveillanceCameraMonitorSystem _surveillanceCameraMonitorSystem;

    [ViewVariables]
    private SurveillanceCameraMonitorWindow? _window;

    [ViewVariables]
    private EntityUid? _currentCamera;

    // <Onyx-Fix>
    [ViewVariables]
    private SurveillanceCameraMonitorUiState? _lastState;

    [ViewVariables]
    private bool _retryPendingState;
    // </Onyx-Fix>

    public SurveillanceCameraMonitorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _eyeLerpingSystem = EntMan.System<EyeLerpingSystem>();
        _surveillanceCameraMonitorSystem = EntMan.System<SurveillanceCameraMonitorSystem>();
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<SurveillanceCameraMonitorWindow>();

        _window.CameraSelected += OnCameraSelected;
        _window.CameraRefresh += OnCameraRefresh;
        _window.SubnetRefresh += OnSubnetRefresh;
        _window.CameraSwitchTimer += OnCameraSwitchTimer;
        _window.CameraDisconnect += OnCameraDisconnect;

        _window.SetEntity(Owner); // Goobstation
    }

    private void OnCameraSelected(string address)
    {
        SendMessage(new SurveillanceCameraMonitorSwitchMessage(address));
    }

    private void OnCameraSwitchTimer()
    {
        _surveillanceCameraMonitorSystem.AddTimer(Owner, _window!.OnSwitchTimerComplete);
    }

    private void OnCameraRefresh()
    {
        SendMessage(new SurveillanceCameraRefreshCamerasMessage());
    }

    private void OnSubnetRefresh()
    {
        SendMessage(new SurveillanceCameraRefreshSubnetsMessage());
    }

    private void OnCameraDisconnect()
    {
        SendMessage(new SurveillanceCameraDisconnectMessage());
    }

    // <Onyx-Fix edited>
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not SurveillanceCameraMonitorUiState cast)
        {
            return;
        }

        _lastState = cast;
        TryApplyState(cast);
    }

    public override void Update()
    {
        base.Update();

        if (!_retryPendingState || _lastState == null)
            return;

        TryApplyState(_lastState);
    }

    private void TryApplyState(SurveillanceCameraMonitorUiState cast)
    {
        if (_window == null)
            return;

        EntityUid? active = null;

        if (cast.ActiveCamera != null && EntMan.TryGetEntity(cast.ActiveCamera, out var activeEnt))
            active = activeEnt;

        EntMan.TryGetComponent<TransformComponent>(Owner, out var xform); // Goobstation
        var monitor = Owner; // Goobstation
        var monitorCoords = xform?.Coordinates; // Goobstation

        _retryPendingState = cast.ActiveCamera != null && active == null;

        if (active == null)
        {
            _window.UpdateState(null, cast.ActiveAddress, cast.Cameras, cast.MobileCameras, monitor, monitorCoords); // Goobstation

            ClearCurrentCamera();
            return;
        }

        if (!EntMan.TryGetComponent<EyeComponent>(active.Value, out var eye))
        {
            _retryPendingState = true;

            if (_currentCamera != null && _currentCamera != active)
                ClearCurrentCamera();

            _window.UpdateState(null, cast.ActiveAddress, cast.Cameras, cast.MobileCameras, monitor, monitorCoords); // Goobstation
            return;
        }

        if (_currentCamera == null)
        {
            _eyeLerpingSystem.AddEye(active.Value);
            _currentCamera = active;
        }
        else if (_currentCamera != active)
        {
            _eyeLerpingSystem.RemoveEye(_currentCamera.Value);
            _eyeLerpingSystem.AddEye(active.Value);
            _currentCamera = active;
        }

        _window.UpdateState(eye.Eye, cast.ActiveAddress, cast.Cameras, cast.MobileCameras, monitor, monitorCoords); // Goobstation
    }

    private void ClearCurrentCamera()
    {
        if (_currentCamera == null)
            return;

        _surveillanceCameraMonitorSystem.RemoveTimer(Owner);
        _eyeLerpingSystem.RemoveEye(_currentCamera.Value);
        _currentCamera = null;
    }
    // </Onyx-Fix edited>

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (_currentCamera != null)
        {
            _eyeLerpingSystem.RemoveEye(_currentCamera.Value);
            _currentCamera = null;
        }

        if (disposing)
        {
            _window?.Dispose();
        }
    }
}
