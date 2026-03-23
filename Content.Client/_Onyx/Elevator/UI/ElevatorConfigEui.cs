using Content.Client.Eui;
using Content.Shared._Onyx.Elevator;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._Onyx.Elevator.UI;

[UsedImplicitly]
public sealed class ElevatorConfigEui : BaseEui
{
    private readonly ElevatorConfigWindow _window;

    public ElevatorConfigEui()
    {
        _window = new ElevatorConfigWindow(this);
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
    }

    public override void Opened()
    {
        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        _window.SetState((ElevatorConfigEuiState) state);
    }

    public void SaveElevator(ElevatorConfigElevatorData data)
    {
        SendMessage(new ElevatorConfigEuiMsg.SaveElevator(data));
    }

    public void SaveButton(ElevatorConfigButtonData data)
    {
        SendMessage(new ElevatorConfigEuiMsg.SaveButton(data));
    }

    public void SaveDoor(ElevatorConfigDoorData data)
    {
        SendMessage(new ElevatorConfigEuiMsg.SaveDoor(data));
    }

    public void SavePoint(ElevatorConfigPointData data)
    {
        SendMessage(new ElevatorConfigEuiMsg.SavePoint(data));
    }
}
