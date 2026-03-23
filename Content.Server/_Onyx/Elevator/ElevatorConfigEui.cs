using System;
using System.Collections.Generic;
using Content.Server.EUI;
using Content.Shared._Onyx.Elevator;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;

namespace Content.Server._Onyx.Elevator;

[UsedImplicitly]
public sealed class ElevatorConfigEui : BaseEui
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    private readonly EntityUid _target;

    public ElevatorConfigEui(EntityUid target)
    {
        IoCManager.InjectDependencies(this);
        _target = target;
    }

    public override void Opened()
    {
        base.Opened();
        StateDirty();
    }

    public override EuiStateBase GetNewState()
    {
        if (!_entityManager.EntityExists(_target))
        {
            return new ElevatorConfigEuiState(NetEntity.Invalid, "<deleted>", null, null, null, null);
        }

        var netEntity = _entityManager.GetNetEntity(_target);
        var name = _entityManager.GetComponent<MetaDataComponent>(_target).EntityName;

        ElevatorConfigElevatorData? elevator = null;
        ElevatorConfigButtonData? button = null;
        ElevatorConfigDoorData? door = null;
        ElevatorConfigPointData? point = null;

        if (_entityManager.TryGetComponent(_target, out ElevatorComponent? elevatorComp))
        {
            elevator = new ElevatorConfigElevatorData(
                elevatorComp.ElevatorId,
                elevatorComp.CurrentFloor,
                new List<string>(elevatorComp.Floors),
                elevatorComp.IntermediateFloorId,
                (float) elevatorComp.SendDelay.TotalSeconds,
                (float) elevatorComp.IntermediateDelay.TotalSeconds,
                (float) elevatorComp.DoorCloseDelay.TotalSeconds,
                GetSoundString(elevatorComp.StartSound),
                GetSoundString(elevatorComp.TravelSound),
                GetSoundString(elevatorComp.ArrivalSound),
                GetSoundString(elevatorComp.AlarmSound),
                elevatorComp.DoorBlockCheckRange,
                elevatorComp.MaxEntitiesToTeleport,
                elevatorComp.TransferGases,
                elevatorComp.ClearGases,
                elevatorComp.KillEntitiesInTargetArea,
                elevatorComp.ForceStandardAtmosphere);
        }

        if (_entityManager.TryGetComponent(_target, out ElevatorButtonComponent? buttonComp))
        {
            button = new ElevatorConfigButtonData(
                buttonComp.ElevatorId,
                (int) buttonComp.ButtonType,
                buttonComp.Floor);
        }

        if (_entityManager.TryGetComponent(_target, out ElevatorDoorComponent? doorComp))
        {
            door = new ElevatorConfigDoorData(
                doorComp.ElevatorId,
                doorComp.Floor);
        }

        if (_entityManager.TryGetComponent(_target, out ElevatorPointComponent? pointComp))
        {
            point = new ElevatorConfigPointData(pointComp.FloorId);
        }

        return new ElevatorConfigEuiState(netEntity, name, elevator, button, door, point);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (!_entityManager.EntityExists(_target))
        {
            Close();
            return;
        }

        switch (msg)
        {
            case ElevatorConfigEuiMsg.SaveElevator saveElevator:
                SaveElevator(saveElevator.Data);
                break;
            case ElevatorConfigEuiMsg.SaveButton saveButton:
                SaveButton(saveButton.Data);
                break;
            case ElevatorConfigEuiMsg.SaveDoor saveDoor:
                SaveDoor(saveDoor.Data);
                break;
            case ElevatorConfigEuiMsg.SavePoint savePoint:
                SavePoint(savePoint.Data);
                break;
        }
    }

    private void SaveElevator(ElevatorConfigElevatorData data)
    {
        if (!_entityManager.TryGetComponent(_target, out ElevatorComponent? comp))
            return;

        comp.ElevatorId = data.ElevatorId.Trim();
        comp.CurrentFloor = data.CurrentFloor.Trim();
        comp.IntermediateFloorId = data.IntermediateFloorId.Trim();
        comp.SendDelay = TimeSpan.FromSeconds(MathF.Max(0f, data.SendDelaySeconds));
        comp.IntermediateDelay = TimeSpan.FromSeconds(MathF.Max(0f, data.IntermediateDelaySeconds));
        comp.DoorCloseDelay = TimeSpan.FromSeconds(MathF.Max(0f, data.DoorCloseDelaySeconds));
        comp.DoorBlockCheckRange = MathF.Max(0f, data.DoorBlockCheckRange);
        comp.MaxEntitiesToTeleport = Math.Max(0, data.MaxEntitiesToTeleport);
        comp.TransferGases = data.TransferGases;
        comp.ClearGases = data.ClearGases;
        comp.KillEntitiesInTargetArea = data.KillEntitiesInTargetArea;
        comp.ForceStandardAtmosphere = data.ForceStandardAtmosphere;
        comp.Floors = SanitizeFloors(data.Floors);

        comp.StartSound = ParseSound(data.StartSound, comp.StartSound);
        comp.TravelSound = ParseSound(data.TravelSound, comp.TravelSound);
        comp.ArrivalSound = ParseSound(data.ArrivalSound, comp.ArrivalSound);
        comp.AlarmSound = ParseSound(data.AlarmSound, comp.AlarmSound);

        _entityManager.Dirty(_target, comp);
        StateDirty();
    }

    private void SaveButton(ElevatorConfigButtonData data)
    {
        if (!_entityManager.TryGetComponent(_target, out ElevatorButtonComponent? comp))
            return;

        comp.ElevatorId = data.ElevatorId.Trim();
        comp.Floor = data.Floor.Trim();
        comp.ButtonType = data.ButtonType switch
        {
            0 => ElevatorButtonType.CallButton,
            1 => ElevatorButtonType.SendElevatorDown,
            2 => ElevatorButtonType.SendElevatorUp,
            _ => comp.ButtonType
        };

        _entityManager.Dirty(_target, comp);
        StateDirty();
    }

    private void SaveDoor(ElevatorConfigDoorData data)
    {
        if (!_entityManager.TryGetComponent(_target, out ElevatorDoorComponent? comp))
            return;

        comp.ElevatorId = data.ElevatorId.Trim();
        comp.Floor = data.Floor.Trim();

        _entityManager.Dirty(_target, comp);
        StateDirty();
    }

    private void SavePoint(ElevatorConfigPointData data)
    {
        if (!_entityManager.TryGetComponent(_target, out ElevatorPointComponent? comp))
            return;

        comp.FloorId = data.FloorId.Trim();

        _entityManager.Dirty(_target, comp);
        StateDirty();
    }

    private static List<string> SanitizeFloors(List<string> floors)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var floor in floors)
        {
            var trimmed = floor.Trim();
            if (trimmed.Length == 0 || !seen.Add(trimmed))
                continue;

            result.Add(trimmed);
        }

        return result;
    }

    private static SoundSpecifier ParseSound(string value, SoundSpecifier fallback)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return fallback;

        const string collectionPrefix = "collection:";
        if (trimmed.StartsWith(collectionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var collectionId = trimmed[collectionPrefix.Length..].Trim();
            return collectionId.Length == 0 ? fallback : new SoundCollectionSpecifier(collectionId);
        }

        return new SoundPathSpecifier(trimmed);
    }

    private static string GetSoundString(SoundSpecifier specifier)
    {
        return specifier switch
        {
            SoundPathSpecifier path => path.Path.ToString(),
            SoundCollectionSpecifier collection => $"collection:{collection.Collection}",
            _ => string.Empty
        };
    }
}
