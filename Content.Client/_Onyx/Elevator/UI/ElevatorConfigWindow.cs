using System.Collections.Generic;
using System.Numerics;
using Content.Shared._Onyx.Elevator;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Localization;

namespace Content.Client._Onyx.Elevator.UI;

public sealed class ElevatorConfigWindow : DefaultWindow
{
    private readonly ElevatorConfigEui _ui;
    private readonly Label _statusLabel;

    private readonly BoxContainer _elevatorEditor;
    private readonly Label _elevatorMissingLabel;
    private readonly LineEdit _elevatorIdEdit;
    private readonly LineEdit _currentFloorEdit;
    private readonly LineEdit _intermediateFloorEdit;
    private readonly FloatSpinBox _sendDelaySpin;
    private readonly FloatSpinBox _intermediateDelaySpin;
    private readonly FloatSpinBox _doorCloseDelaySpin;
    private readonly LineEdit _startSoundEdit;
    private readonly LineEdit _travelSoundEdit;
    private readonly LineEdit _arrivalSoundEdit;
    private readonly LineEdit _alarmSoundEdit;
    private readonly FloatSpinBox _doorBlockRangeSpin;
    private readonly SpinBox _maxEntitiesSpin;
    private readonly CheckBox _transferGasesCheck;
    private readonly CheckBox _clearGasesCheck;
    private readonly CheckBox _killEntitiesCheck;
    private readonly CheckBox _forceAtmosCheck;
    private readonly LineEdit _newFloorEdit;
    private readonly ItemList _floorsList;
    private readonly Button _removeFloorButton;
    private readonly Button _moveFloorUpButton;
    private readonly Button _moveFloorDownButton;
    private readonly List<string> _floors = new();
    private int? _selectedFloor;

    private readonly BoxContainer _buttonEditor;
    private readonly Label _buttonMissingLabel;
    private readonly LineEdit _buttonElevatorIdEdit;
    private readonly OptionButton _buttonTypeOption;
    private readonly LineEdit _buttonFloorEdit;

    private readonly BoxContainer _doorEditor;
    private readonly Label _doorMissingLabel;
    private readonly LineEdit _doorElevatorIdEdit;
    private readonly LineEdit _doorFloorEdit;

    private readonly BoxContainer _pointEditor;
    private readonly Label _pointMissingLabel;
    private readonly LineEdit _pointFloorIdEdit;

    public ElevatorConfigWindow(ElevatorConfigEui ui)
    {
        _ui = ui;
        MinSize = new Vector2(760, 620);
        Title = Loc.GetString("elevator-config-ui-title");

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            SeparationOverride = 6
        };

        _statusLabel = new Label
        {
            Text = Loc.GetString("elevator-config-ui-status-ready"),
            HorizontalExpand = true
        };
        root.AddChild(_statusLabel);

        var tabs = new TabContainer
        {
            VerticalExpand = true
        };

        root.AddChild(tabs);
        Contents.AddChild(root);

        var elevatorTabName = Loc.GetString("elevator-config-ui-component-elevator");
        var elevatorTab = BuildComponentTab(elevatorTabName, out _elevatorEditor, out _elevatorMissingLabel);
        tabs.AddChild(elevatorTab);
        TabContainer.SetTabTitle(elevatorTab, elevatorTabName);

        _elevatorIdEdit = new LineEdit();
        _currentFloorEdit = new LineEdit();
        _intermediateFloorEdit = new LineEdit();
        _sendDelaySpin = MakeFloatSpin(0.1f, 2);
        _intermediateDelaySpin = MakeFloatSpin(0.1f, 2);
        _doorCloseDelaySpin = MakeFloatSpin(0.05f, 2);
        _startSoundEdit = new LineEdit();
        _travelSoundEdit = new LineEdit();
        _arrivalSoundEdit = new LineEdit();
        _alarmSoundEdit = new LineEdit();
        _doorBlockRangeSpin = MakeFloatSpin(0.01f, 2);
        _maxEntitiesSpin = MakeIntSpin();
        _transferGasesCheck = new CheckBox { Text = Loc.GetString("elevator-config-ui-transfer-gases") };
        _clearGasesCheck = new CheckBox { Text = Loc.GetString("elevator-config-ui-clear-gases") };
        _killEntitiesCheck = new CheckBox { Text = Loc.GetString("elevator-config-ui-kill-entities") };
        _forceAtmosCheck = new CheckBox { Text = Loc.GetString("elevator-config-ui-force-atmos") };
        _newFloorEdit = new LineEdit();
        _floorsList = new ItemList
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            MinSize = new Vector2(280, 140)
        };

        _elevatorEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-elevator-id"), _elevatorIdEdit));
        _elevatorEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-current-floor"), _currentFloorEdit));
        _elevatorEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-intermediate-floor"), _intermediateFloorEdit));
        _elevatorEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-send-delay"), _sendDelaySpin));
        _elevatorEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-intermediate-delay"), _intermediateDelaySpin));
        _elevatorEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-door-close-delay"), _doorCloseDelaySpin));
        _elevatorEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-start-sound"), _startSoundEdit));
        _elevatorEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-travel-sound"), _travelSoundEdit));
        _elevatorEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-arrival-sound"), _arrivalSoundEdit));
        _elevatorEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-alarm-sound"), _alarmSoundEdit));
        _elevatorEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-door-block-range"), _doorBlockRangeSpin));
        _elevatorEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-max-entities"), _maxEntitiesSpin));
        _elevatorEditor.AddChild(_transferGasesCheck);
        _elevatorEditor.AddChild(_clearGasesCheck);
        _elevatorEditor.AddChild(_killEntitiesCheck);
        _elevatorEditor.AddChild(_forceAtmosCheck);
        _elevatorEditor.AddChild(new Label { Text = Loc.GetString("elevator-config-ui-floors-list"), HorizontalExpand = true });

        var addFloorButton = new Button
        {
            Text = Loc.GetString("elevator-config-ui-add-floor")
        };
        addFloorButton.OnPressed += _ => AddFloor();
        _newFloorEdit.OnTextEntered += _ => AddFloor();
        _elevatorEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-new-floor"), _newFloorEdit, addFloorButton));

        var floorButtons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4
        };
        _removeFloorButton = new Button { Text = Loc.GetString("elevator-config-ui-remove") };
        _moveFloorUpButton = new Button { Text = Loc.GetString("elevator-config-ui-move-up") };
        _moveFloorDownButton = new Button { Text = Loc.GetString("elevator-config-ui-move-down") };
        floorButtons.AddChild(_removeFloorButton);
        floorButtons.AddChild(_moveFloorUpButton);
        floorButtons.AddChild(_moveFloorDownButton);

        var floorPanel = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            MinSize = new Vector2(0, 160),
            HorizontalExpand = true,
            VerticalExpand = true
        };
        floorPanel.AddChild(_floorsList);
        floorPanel.AddChild(floorButtons);
        _elevatorEditor.AddChild(floorPanel);

        _floorsList.OnItemSelected += args =>
        {
            _selectedFloor = args.ItemIndex;
            UpdateFloorButtons();
        };
        _floorsList.OnItemDeselected += _ =>
        {
            _selectedFloor = null;
            UpdateFloorButtons();
        };

        _removeFloorButton.OnPressed += _ => RemoveSelectedFloor();
        _moveFloorUpButton.OnPressed += _ => MoveSelectedFloor(-1);
        _moveFloorDownButton.OnPressed += _ => MoveSelectedFloor(1);
        UpdateFloorButtons();

        var saveElevatorButton = new Button
        {
            Text = Loc.GetString("elevator-config-ui-save-elevator"),
            HorizontalAlignment = HAlignment.Right
        };
        saveElevatorButton.OnPressed += _ => SaveElevator();
        _elevatorEditor.AddChild(saveElevatorButton);

        var buttonTabName = Loc.GetString("elevator-config-ui-component-button");
        var buttonTab = BuildComponentTab(buttonTabName, out _buttonEditor, out _buttonMissingLabel);
        tabs.AddChild(buttonTab);
        TabContainer.SetTabTitle(buttonTab, buttonTabName);

        _buttonElevatorIdEdit = new LineEdit();
        _buttonTypeOption = new OptionButton();
        _buttonFloorEdit = new LineEdit();

        _buttonTypeOption.AddItem(Loc.GetString("elevator-config-ui-button-type-call"), 0);
        _buttonTypeOption.AddItem(Loc.GetString("elevator-config-ui-button-type-down"), 1);
        _buttonTypeOption.AddItem(Loc.GetString("elevator-config-ui-button-type-up"), 2);
        _buttonTypeOption.OnItemSelected += args => _buttonTypeOption.SelectId(args.Id);

        _buttonEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-elevator-id"), _buttonElevatorIdEdit));
        _buttonEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-button-type"), _buttonTypeOption));
        _buttonEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-floor"), _buttonFloorEdit));

        var saveButtonComponentButton = new Button
        {
            Text = Loc.GetString("elevator-config-ui-save-button"),
            HorizontalAlignment = HAlignment.Right
        };
        saveButtonComponentButton.OnPressed += _ => SaveButton();
        _buttonEditor.AddChild(saveButtonComponentButton);

        var doorTabName = Loc.GetString("elevator-config-ui-component-door");
        var doorTab = BuildComponentTab(doorTabName, out _doorEditor, out _doorMissingLabel);
        tabs.AddChild(doorTab);
        TabContainer.SetTabTitle(doorTab, doorTabName);

        _doorElevatorIdEdit = new LineEdit();
        _doorFloorEdit = new LineEdit();

        _doorEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-elevator-id"), _doorElevatorIdEdit));
        _doorEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-floor"), _doorFloorEdit));

        var saveDoorButton = new Button
        {
            Text = Loc.GetString("elevator-config-ui-save-door"),
            HorizontalAlignment = HAlignment.Right
        };
        saveDoorButton.OnPressed += _ => SaveDoor();
        _doorEditor.AddChild(saveDoorButton);

        var pointTabName = Loc.GetString("elevator-config-ui-component-point");
        var pointTab = BuildComponentTab(pointTabName, out _pointEditor, out _pointMissingLabel);
        tabs.AddChild(pointTab);
        TabContainer.SetTabTitle(pointTab, pointTabName);

        _pointFloorIdEdit = new LineEdit();
        _pointEditor.AddChild(MakeRow(Loc.GetString("elevator-config-ui-label-floor-id"), _pointFloorIdEdit));

        var savePointButton = new Button
        {
            Text = Loc.GetString("elevator-config-ui-save-point"),
            HorizontalAlignment = HAlignment.Right
        };
        savePointButton.OnPressed += _ => SavePoint();
        _pointEditor.AddChild(savePointButton);
    }

    public void SetState(ElevatorConfigEuiState state)
    {
        Title = Loc.GetString("elevator-config-ui-title-target", ("name", state.EntityName), ("uid", state.Target));
        SetStatus(Loc.GetString("elevator-config-ui-status-updated"));

        SetElevatorState(state.Elevator);
        SetButtonState(state.Button);
        SetDoorState(state.Door);
        SetPointState(state.Point);
    }

    private void SetElevatorState(ElevatorConfigElevatorData? data)
    {
        var exists = data != null;
        _elevatorEditor.Visible = exists;
        _elevatorMissingLabel.Visible = !exists;

        if (data == null)
            return;

        _elevatorIdEdit.Text = data.ElevatorId;
        _currentFloorEdit.Text = data.CurrentFloor;
        _intermediateFloorEdit.Text = data.IntermediateFloorId;
        _sendDelaySpin.Value = data.SendDelaySeconds;
        _intermediateDelaySpin.Value = data.IntermediateDelaySeconds;
        _doorCloseDelaySpin.Value = data.DoorCloseDelaySeconds;
        _startSoundEdit.Text = data.StartSound;
        _travelSoundEdit.Text = data.TravelSound;
        _arrivalSoundEdit.Text = data.ArrivalSound;
        _alarmSoundEdit.Text = data.AlarmSound;
        _doorBlockRangeSpin.Value = data.DoorBlockCheckRange;
        _maxEntitiesSpin.OverrideValue(data.MaxEntitiesToTeleport);
        _transferGasesCheck.Pressed = data.TransferGases;
        _clearGasesCheck.Pressed = data.ClearGases;
        _killEntitiesCheck.Pressed = data.KillEntitiesInTargetArea;
        _forceAtmosCheck.Pressed = data.ForceStandardAtmosphere;

        _floors.Clear();
        _floors.AddRange(data.Floors);
        RefreshFloorsList();
    }

    private void SetButtonState(ElevatorConfigButtonData? data)
    {
        var exists = data != null;
        _buttonEditor.Visible = exists;
        _buttonMissingLabel.Visible = !exists;

        if (data == null)
            return;

        _buttonElevatorIdEdit.Text = data.ElevatorId;
        _buttonFloorEdit.Text = data.Floor;
        if (!_buttonTypeOption.TrySelectId(data.ButtonType))
            _buttonTypeOption.SelectId(0);
    }

    private void SetDoorState(ElevatorConfigDoorData? data)
    {
        var exists = data != null;
        _doorEditor.Visible = exists;
        _doorMissingLabel.Visible = !exists;

        if (data == null)
            return;

        _doorElevatorIdEdit.Text = data.ElevatorId;
        _doorFloorEdit.Text = data.Floor;
    }

    private void SetPointState(ElevatorConfigPointData? data)
    {
        var exists = data != null;
        _pointEditor.Visible = exists;
        _pointMissingLabel.Visible = !exists;

        if (data == null)
            return;

        _pointFloorIdEdit.Text = data.FloorId;
    }

    private void SaveElevator()
    {
        var data = new ElevatorConfigElevatorData(
            _elevatorIdEdit.Text.Trim(),
            _currentFloorEdit.Text.Trim(),
            new List<string>(_floors),
            _intermediateFloorEdit.Text.Trim(),
            _sendDelaySpin.Value,
            _intermediateDelaySpin.Value,
            _doorCloseDelaySpin.Value,
            _startSoundEdit.Text.Trim(),
            _travelSoundEdit.Text.Trim(),
            _arrivalSoundEdit.Text.Trim(),
            _alarmSoundEdit.Text.Trim(),
            _doorBlockRangeSpin.Value,
            _maxEntitiesSpin.Value,
            _transferGasesCheck.Pressed,
            _clearGasesCheck.Pressed,
            _killEntitiesCheck.Pressed,
            _forceAtmosCheck.Pressed);

        _ui.SaveElevator(data);
        SetStatus(Loc.GetString("elevator-config-ui-status-sent-elevator"));
    }

    private void SaveButton()
    {
        var buttonType = _buttonTypeOption.SelectedId >= 0 ? _buttonTypeOption.SelectedId : 0;

        var data = new ElevatorConfigButtonData(
            _buttonElevatorIdEdit.Text.Trim(),
            buttonType,
            _buttonFloorEdit.Text.Trim());

        _ui.SaveButton(data);
        SetStatus(Loc.GetString("elevator-config-ui-status-sent-button"));
    }

    private void SaveDoor()
    {
        var data = new ElevatorConfigDoorData(
            _doorElevatorIdEdit.Text.Trim(),
            _doorFloorEdit.Text.Trim());

        _ui.SaveDoor(data);
        SetStatus(Loc.GetString("elevator-config-ui-status-sent-door"));
    }

    private void SavePoint()
    {
        var data = new ElevatorConfigPointData(_pointFloorIdEdit.Text.Trim());
        _ui.SavePoint(data);
        SetStatus(Loc.GetString("elevator-config-ui-status-sent-point"));
    }

    private void AddFloor()
    {
        var floor = _newFloorEdit.Text.Trim();
        if (floor.Length == 0)
        {
            SetStatus(Loc.GetString("elevator-config-ui-status-floor-empty"));
            return;
        }

        if (_floors.Contains(floor))
        {
            SetStatus(Loc.GetString("elevator-config-ui-status-floor-exists"));
            return;
        }

        _floors.Add(floor);
        _newFloorEdit.Text = string.Empty;
        RefreshFloorsList(_floors.Count - 1);
        SetStatus(Loc.GetString("elevator-config-ui-status-floor-added", ("floor", floor)));
    }

    private void RemoveSelectedFloor()
    {
        if (_selectedFloor is not { } selected || selected < 0 || selected >= _floors.Count)
            return;

        var removed = _floors[selected];
        _floors.RemoveAt(selected);
        var newSelection = selected >= _floors.Count ? _floors.Count - 1 : selected;
        RefreshFloorsList(newSelection >= 0 ? newSelection : null);
        SetStatus(Loc.GetString("elevator-config-ui-status-floor-removed", ("floor", removed)));
    }

    private void MoveSelectedFloor(int offset)
    {
        if (_selectedFloor is not { } selected || selected < 0 || selected >= _floors.Count)
            return;

        var target = selected + offset;
        if (target < 0 || target >= _floors.Count)
            return;

        (_floors[selected], _floors[target]) = (_floors[target], _floors[selected]);
        RefreshFloorsList(target);
        SetStatus(Loc.GetString("elevator-config-ui-status-floor-order-updated"));
    }

    private void RefreshFloorsList(int? selectIndex = null)
    {
        _floorsList.Clear();
        _selectedFloor = null;

        for (var i = 0; i < _floors.Count; i++)
        {
            _floorsList.Add(new ItemList.Item(_floorsList)
            {
                Text = $"{i + 1}. {_floors[i]}",
                Metadata = i
            });
        }

        if (selectIndex is { } index && index >= 0 && index < _floorsList.Count)
        {
            _floorsList[index].Selected = true;
        }

        UpdateFloorButtons();
    }

    private void UpdateFloorButtons()
    {
        var hasSelection = _selectedFloor is { } index && index >= 0 && index < _floors.Count;

        _removeFloorButton.Disabled = !hasSelection;
        _moveFloorUpButton.Disabled = !hasSelection || _selectedFloor == 0;
        _moveFloorDownButton.Disabled = !hasSelection || _selectedFloor == _floors.Count - 1;
    }

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
    }

    private static Control BuildComponentTab(string componentName, out BoxContainer editor, out Label missingLabel)
    {
        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true
        };

        var scroll = new ScrollContainer
        {
            VerticalExpand = true
        };
        root.AddChild(scroll);

        var body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            VerticalExpand = true
        };
        scroll.AddChild(body);

        missingLabel = new Label
        {
            Text = Loc.GetString("elevator-config-ui-component-missing", ("component", componentName)),
            Visible = false
        };
        body.AddChild(missingLabel);

        editor = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6
        };
        body.AddChild(editor);

        return root;
    }

    private static FloatSpinBox MakeFloatSpin(float step, byte precision)
    {
        var spin = new FloatSpinBox(step, precision)
        {
            HorizontalExpand = true
        };
        spin.IsValid = value => value >= 0;
        return spin;
    }

    private static SpinBox MakeIntSpin()
    {
        var spin = new SpinBox
        {
            HorizontalExpand = true
        };
        spin.IsValid = value => value >= 0;
        spin.InitDefaultButtons();
        return spin;
    }

    private static BoxContainer MakeRow(string labelText, Control mainControl, Control? trailingControl = null)
    {
        mainControl.HorizontalExpand = true;

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true
        };

        row.AddChild(new Label
        {
            Text = labelText,
            MinSize = new Vector2(220, 0),
            VerticalAlignment = VAlignment.Center
        });
        row.AddChild(mainControl);

        if (trailingControl != null)
            row.AddChild(trailingControl);

        return row;
    }
}
