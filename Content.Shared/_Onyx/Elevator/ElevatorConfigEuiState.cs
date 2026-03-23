using System.Collections.Generic;
using Content.Shared.Eui;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._Onyx.Elevator;

[Serializable, NetSerializable]
public sealed class ElevatorConfigEuiState : EuiStateBase
{
    public readonly NetEntity Target;
    public readonly string EntityName;
    public readonly ElevatorConfigElevatorData? Elevator;
    public readonly ElevatorConfigButtonData? Button;
    public readonly ElevatorConfigDoorData? Door;
    public readonly ElevatorConfigPointData? Point;

    public ElevatorConfigEuiState(
        NetEntity target,
        string entityName,
        ElevatorConfigElevatorData? elevator,
        ElevatorConfigButtonData? button,
        ElevatorConfigDoorData? door,
        ElevatorConfigPointData? point)
    {
        Target = target;
        EntityName = entityName;
        Elevator = elevator;
        Button = button;
        Door = door;
        Point = point;
    }
}

[Serializable, NetSerializable]
public sealed class ElevatorConfigElevatorData
{
    public readonly string ElevatorId;
    public readonly string CurrentFloor;
    public readonly List<string> Floors;
    public readonly string IntermediateFloorId;
    public readonly float SendDelaySeconds;
    public readonly float IntermediateDelaySeconds;
    public readonly float DoorCloseDelaySeconds;
    public readonly string StartSound;
    public readonly string TravelSound;
    public readonly string ArrivalSound;
    public readonly string AlarmSound;
    public readonly float DoorBlockCheckRange;
    public readonly int MaxEntitiesToTeleport;
    public readonly bool TransferGases;
    public readonly bool ClearGases;
    public readonly bool KillEntitiesInTargetArea;
    public readonly bool ForceStandardAtmosphere;

    public ElevatorConfigElevatorData(
        string elevatorId,
        string currentFloor,
        List<string> floors,
        string intermediateFloorId,
        float sendDelaySeconds,
        float intermediateDelaySeconds,
        float doorCloseDelaySeconds,
        string startSound,
        string travelSound,
        string arrivalSound,
        string alarmSound,
        float doorBlockCheckRange,
        int maxEntitiesToTeleport,
        bool transferGases,
        bool clearGases,
        bool killEntitiesInTargetArea,
        bool forceStandardAtmosphere)
    {
        ElevatorId = elevatorId;
        CurrentFloor = currentFloor;
        Floors = floors;
        IntermediateFloorId = intermediateFloorId;
        SendDelaySeconds = sendDelaySeconds;
        IntermediateDelaySeconds = intermediateDelaySeconds;
        DoorCloseDelaySeconds = doorCloseDelaySeconds;
        StartSound = startSound;
        TravelSound = travelSound;
        ArrivalSound = arrivalSound;
        AlarmSound = alarmSound;
        DoorBlockCheckRange = doorBlockCheckRange;
        MaxEntitiesToTeleport = maxEntitiesToTeleport;
        TransferGases = transferGases;
        ClearGases = clearGases;
        KillEntitiesInTargetArea = killEntitiesInTargetArea;
        ForceStandardAtmosphere = forceStandardAtmosphere;
    }
}

[Serializable, NetSerializable]
public sealed class ElevatorConfigButtonData
{
    public readonly string ElevatorId;
    public readonly int ButtonType;
    public readonly string Floor;

    public ElevatorConfigButtonData(string elevatorId, int buttonType, string floor)
    {
        ElevatorId = elevatorId;
        ButtonType = buttonType;
        Floor = floor;
    }
}

[Serializable, NetSerializable]
public sealed class ElevatorConfigDoorData
{
    public readonly string ElevatorId;
    public readonly string Floor;

    public ElevatorConfigDoorData(string elevatorId, string floor)
    {
        ElevatorId = elevatorId;
        Floor = floor;
    }
}

[Serializable, NetSerializable]
public sealed class ElevatorConfigPointData
{
    public readonly string FloorId;

    public ElevatorConfigPointData(string floorId)
    {
        FloorId = floorId;
    }
}

public static class ElevatorConfigEuiMsg
{
    [Serializable, NetSerializable]
    public sealed class SaveElevator : EuiMessageBase
    {
        public readonly ElevatorConfigElevatorData Data;

        public SaveElevator(ElevatorConfigElevatorData data)
        {
            Data = data;
        }
    }

    [Serializable, NetSerializable]
    public sealed class SaveButton : EuiMessageBase
    {
        public readonly ElevatorConfigButtonData Data;

        public SaveButton(ElevatorConfigButtonData data)
        {
            Data = data;
        }
    }

    [Serializable, NetSerializable]
    public sealed class SaveDoor : EuiMessageBase
    {
        public readonly ElevatorConfigDoorData Data;

        public SaveDoor(ElevatorConfigDoorData data)
        {
            Data = data;
        }
    }

    [Serializable, NetSerializable]
    public sealed class SavePoint : EuiMessageBase
    {
        public readonly ElevatorConfigPointData Data;

        public SavePoint(ElevatorConfigPointData data)
        {
            Data = data;
        }
    }
}
