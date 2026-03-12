using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Actions;
using Content.Shared.Body.Events;
using Content.Shared.Body.Part;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Components;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Popups;
using Content.Shared.Containers;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Goobstation.Shared.Augments;
using Content.Shared.Item;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Utility;
using Robust.Shared.Timing;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentItemPanelSystem : EntitySystem
{
    [Dependency] private readonly AugmentSystem _augment = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAugmentPowerCellSystem _augmentPower = default!;
    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;

    private EntityQuery<HandsComponent> _handsQuery;
    private EntityQuery<BodyPartComponent> _partQuery;

    public const string DefaultActionPrototype = "ActionAugmentToggleItemPanel";
    public const string LeftActionPrototype = "ActionAugmentToggleItemPanelLeft";
    public const string RightActionPrototype = "ActionAugmentToggleItemPanelRight";

    public override void Initialize()
    {
        base.Initialize();

        _handsQuery = GetEntityQuery<HandsComponent>();
        _partQuery = GetEntityQuery<BodyPartComponent>();

        SubscribeLocalEvent<AugmentItemPanelComponent, OrganAddedToBodyEvent>(OnOrganAddedToBody);
        SubscribeLocalEvent<AugmentItemPanelComponent, OrganRemovedFromBodyEvent>(OnOrganRemovedFromBody);
        SubscribeLocalEvent<AugmentItemPanelComponent, BodyPartAddedEvent>(OnBodyPartAdded);
        SubscribeLocalEvent<AugmentItemPanelComponent, BodyPartRemovedEvent>(OnBodyPartRemoved);
        SubscribeLocalEvent<AugmentItemPanelComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<AugmentItemPanelComponent, AugmentActionEvent>(OnToggleItem);
        SubscribeLocalEvent<AugmentItemPanelComponent, AugmentEmpDisabledEvent>(OnEmpDisabled);
        SubscribeLocalEvent<AugmentItemPanelComponent, AugmentManuallyDisabledEvent>(OnManuallyDisabled);
        SubscribeLocalEvent<AugmentItemPanelComponent, AugmentLostPowerEvent>(OnLostPower);
        SubscribeLocalEvent<AugmentItemPanelComponent, CollectAugmentNeuroInterfaceMetricsEvent>(OnCollectMetrics);
    }

    private void OnInit(Entity<AugmentItemPanelComponent> ent, ref ComponentInit args)
    {
        EnsureStorageContainer(ent);
        EnsureStoredItem(ent);
    }

    private void OnOrganAddedToBody(Entity<AugmentItemPanelComponent> ent, ref OrganAddedToBodyEvent args)
    {
        EnsureStoredItem(ent);
        AddActionWithItemIcon(ent, args.Body);
    }

    private void OnOrganRemovedFromBody(Entity<AugmentItemPanelComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        RemovePanelAction(ent.Comp, args.OldBody);
        TryRetractIfEquipped(ent, args.OldBody);
    }

    private void OnBodyPartAdded(Entity<AugmentItemPanelComponent> ent, ref BodyPartAddedEvent args)
    {
        EnsureStoredItem(ent);

        if (args.Part.Comp.Body is not { } body)
            return;

        AddActionWithItemIcon(ent, body);
    }

    private void OnBodyPartRemoved(Entity<AugmentItemPanelComponent> ent, ref BodyPartRemovedEvent args)
    {
        if (args.Part.Comp.Body is not { } oldBody)
            return;

        RemovePanelAction(ent.Comp, oldBody);
        TryRetractIfEquipped(ent, oldBody);
    }

    private void AddActionWithItemIcon(Entity<AugmentItemPanelComponent> ent, EntityUid body)
    {
        var actionPrototype = GetActionPrototypeForAugment(ent);

        if (ent.Comp.ActionEntity is { } actionEntity
            && TryComp<MetaDataComponent>(actionEntity, out var actionMeta)
            && actionMeta.EntityPrototype?.ID != actionPrototype)
        {
            _actions.RemoveAction(body, actionEntity);
            QueueDel(actionEntity);
            ent.Comp.ActionEntity = null;
        }

        _actions.AddAction(body, ref ent.Comp.ActionEntity, actionPrototype, ent);

        if (!ent.Comp.ActionEntity.HasValue)
            return;

        if (ent.Comp.Icon != null)
        {
            _actions.SetIcon(ent.Comp.ActionEntity.Value, ent.Comp.Icon);
        }

        _actions.SetUseDelay(ent.Comp.ActionEntity.Value, ent.Comp.ActionCooldown > TimeSpan.Zero
            ? ent.Comp.ActionCooldown
            : null);
    }

    private string GetActionPrototypeForAugment(Entity<AugmentItemPanelComponent> ent)
    {
        if (!_partQuery.TryComp(ent.Owner, out var part))
        {
            var partUid = Transform(ent).ParentUid;
            if (!_partQuery.TryComp(partUid, out part))
                return DefaultActionPrototype;
        }

        return part.Symmetry switch
        {
            BodyPartSymmetry.Left => LeftActionPrototype,
            BodyPartSymmetry.Right => RightActionPrototype,
            _ => DefaultActionPrototype,
        };
    }

    private void OnEmpDisabled(Entity<AugmentItemPanelComponent> ent, ref AugmentEmpDisabledEvent args)
    {
        if (ent.Comp.IsEquipped)
            RetractItem(ent, args.Body);
    }

    private void OnManuallyDisabled(Entity<AugmentItemPanelComponent> ent, ref AugmentManuallyDisabledEvent args)
    {
        if (ent.Comp.IsEquipped)
            RetractItem(ent, args.Body);
    }

    private void OnLostPower(Entity<AugmentItemPanelComponent> ent, ref AugmentLostPowerEvent args)
    {
        if (RequiresPower(ent) && ent.Comp.IsEquipped)
            RetractItem(ent, args.Body);
    }

    private void OnToggleItem(Entity<AugmentItemPanelComponent> ent, ref AugmentActionEvent args)
    {
        if (!TryGetHostBody(ent, out var body))
            return;

        if (TryGetToggleBlockedPopup(ent.Owner, out var popupLocKey))
        {
            _popup.PopupEntity(Loc.GetString(popupLocKey), body, body, PopupType.SmallCaution);
            return;
        }

        var actionPowerCost = GetActionPowerCost(ent);
        if (RequiresPower(ent) &&
            actionPowerCost > 0f &&
            !TryUseChargeBody(body, actionPowerCost))
        {
            return;
        }

        if (ent.Comp.IsEquipped)
        {
            RetractItem(ent, body);
        }
        else
        {
            DeployItem(ent, body);
        }
    }

    private bool TryGetToggleBlockedPopup(EntityUid augment, out string popupLocKey)
    {
        if (HasComp<AugmentNeuroManuallyDisabledComponent>(augment))
        {
            popupLocKey = "augment-disabled-manually";
            return true;
        }

        if (HasComp<AugmentBrainDeactivatedComponent>(augment))
        {
            popupLocKey = "augment-brain-disabled";
            return true;
        }

        if (HasComp<AugmentSuppressedByProjectorsComponent>(augment))
        {
            popupLocKey = "augment-suppression-disabled";
            return true;
        }

        if (HasComp<AugmentEmpDisabledComponent>(augment))
        {
            popupLocKey = "augment-emp-disabled";
            return true;
        }

        popupLocKey = string.Empty;
        return false;
    }

    private void DeployItem(Entity<AugmentItemPanelComponent> ent, EntityUid body)
    {
        if (!_handsQuery.TryComp(body, out var handsComp))
            return;

        if (!EnsureStoredItem(ent))
        {
            _popup.PopupEntity(Loc.GetString("augment-item-panel-cannot-equip"), body, body, PopupType.SmallCaution);
            return;
        }

        var spawnedItem = ent.Comp.SpawnedItem!.Value;
        var storage = EnsureStorageContainer(ent);
        RemComp<UnremoveableComponent>(spawnedItem);
        var preferredHand = GetHandForAugment(ent, body, handsComp);
        var pickedUp = false;
        if (preferredHand != null && _hands.GetHeldItem(body, preferredHand) == null)
            pickedUp = _hands.TryForcePickup((body, handsComp), spawnedItem, preferredHand, checkActionBlocker: false);

        if (!pickedUp)
            pickedUp = _hands.TryForcePickupAnyHand(body, spawnedItem, checkActionBlocker: false, handsComp: handsComp);

        if (!pickedUp)
        {
            _containers.Insert(spawnedItem, storage);

            _popup.PopupEntity(Loc.GetString("augment-item-panel-cannot-equip"), body, body, PopupType.SmallCaution);
            return;
        }

        EnsureComp<UnremoveableComponent>(spawnedItem);
        ApplyExtendHeldAnimation(ent.Comp, spawnedItem);
        if (ent.Comp.ExtendSound != null)
            _audio.PlayPvs(ent.Comp.ExtendSound, body);

        ent.Comp.IsEquipped = true;
        _toggle.TryActivate(ent.Owner);
        StartActionCooldown(ent.Comp);
        _popup.PopupEntity(Loc.GetString("augment-item-panel-deployed", ("item", spawnedItem)), body, body);
    }

    private void RetractItem(Entity<AugmentItemPanelComponent> ent, EntityUid body)
    {
        if (!ent.Comp.SpawnedItem.HasValue)
        {
            ent.Comp.IsEquipped = false;
            return;
        }

        var item = ent.Comp.SpawnedItem.Value;
        var storage = EnsureStorageContainer(ent);
        RemComp<UnremoveableComponent>(item);
        if (!Terminating(item))
        {
            if (_containers.TryGetContainingContainer(item, out var current) && current != storage)
            {
                if (!_containers.Remove(item, current, force: true))
                    return;
            }

            if (!_containers.Insert(item, storage))
                return;
        }

        if (ent.Comp.RetractSound != null)
            _audio.PlayPvs(ent.Comp.RetractSound, body);

        ent.Comp.IsEquipped = false;
        _toggle.TryDeactivate(ent.Owner);
        StartActionCooldown(ent.Comp);

        _popup.PopupEntity(Loc.GetString("augment-item-panel-retracted", ("item", item)), body, body);
    }

    private string? GetHandForAugment(Entity<AugmentItemPanelComponent> ent, EntityUid body, HandsComponent handsComp)
    {
        if (!_partQuery.TryComp(ent.Owner, out var part))
        {
            var partUid = Transform(ent).ParentUid;
            if (!_partQuery.TryComp(partUid, out part))
                return null;
        }

        var targetLocation = part.Symmetry switch
        {
            BodyPartSymmetry.Left => HandLocation.Left,
            BodyPartSymmetry.Right => HandLocation.Right,
            _ => HandLocation.Middle,
        };

        foreach (var (hand, handData) in handsComp.Hands)
        {
            if (handData.Location == targetLocation)
                return hand;
        }

        return null;
    }

    private bool TryGetHostBody(Entity<AugmentItemPanelComponent> ent, out EntityUid body)
    {
        if (_augment.GetBody(ent) is { } currentBody)
        {
            body = currentBody;
            return true;
        }

        body = default;
        return false;
    }

    private bool RequiresPower(Entity<AugmentItemPanelComponent> ent)
    {
        return ent.Comp.RequiresPower
            && (ent.Comp.ExtendPowerCost > 0f || ent.Comp.RetractPowerCost > 0f)
            && (!TryComp<AugmentPowerConfigComponent>(ent.Owner, out var globalConfig) || globalConfig.RequiresPower);
    }

    private void OnCollectMetrics(Entity<AugmentItemPanelComponent> ent, ref CollectAugmentNeuroInterfaceMetricsEvent args)
    {
        if (ent.Comp.RequiresPower && args.PowerEnabled)
        {
            if (ent.Comp.ExtendPowerCost > 0f)
            {
                var extend = AugmentModuleModifierHelpers.ApplyActiveModifiersWithFloor(
                    ent.Comp.ExtendPowerCost,
                    AugmentModuleModifierHelpers.GetItemPanelActivePowerMultiplier(ent.Owner, EntityManager),
                    AugmentModuleModifierHelpers.GetItemPanelActivePower(ent.Owner, EntityManager));
                args.ActivePowerEntries.Add(new NeuroInterfaceMetricEntry("neuro-interface-tooltip-source-power-item-panel-extend", extend));
            }
            if (ent.Comp.RetractPowerCost > 0f)
            {
                var retract = AugmentModuleModifierHelpers.ApplyActiveModifiersWithFloor(
                    ent.Comp.RetractPowerCost,
                    AugmentModuleModifierHelpers.GetItemPanelActivePowerMultiplier(ent.Owner, EntityManager),
                    AugmentModuleModifierHelpers.GetItemPanelActivePower(ent.Owner, EntityManager));
                args.ActivePowerEntries.Add(new NeuroInterfaceMetricEntry("neuro-interface-tooltip-source-power-item-panel-retract", retract));
            }
        }

        if (ent.Comp.EquippedNeuroLoad > 0f)
        {
            var neuro = AugmentModuleModifierHelpers.ApplyActiveModifiersWithFloor(
                ent.Comp.EquippedNeuroLoad,
                AugmentModuleModifierHelpers.GetItemPanelActiveNeuroMultiplier(ent.Owner, EntityManager),
                AugmentModuleModifierHelpers.GetItemPanelActiveNeuro(ent.Owner, EntityManager));
            args.ActiveNeuroLoadEntries.Add(new NeuroInterfaceMetricEntry("neuro-interface-tooltip-source-neuro-item-panel-equipped", neuro));
        }
    }

    private float GetActionPowerCost(Entity<AugmentItemPanelComponent> ent)
    {
        var baseCost = ent.Comp.IsEquipped
            ? ent.Comp.RetractPowerCost
            : ent.Comp.ExtendPowerCost;

        return AugmentModuleModifierHelpers.ApplyActiveModifiersWithFloor(
            baseCost,
            AugmentModuleModifierHelpers.GetItemPanelActivePowerMultiplier(ent.Owner, EntityManager),
            AugmentModuleModifierHelpers.GetItemPanelActivePower(ent.Owner, EntityManager));
    }

    private bool TryUseChargeBody(EntityUid body, float amount)
    {
        if (_augmentPower.GetBodyAugment(body) is not { } slot)
        {
            _popup.PopupEntity(Loc.GetString("augments-no-power-cell-slot"), body, body, PopupType.MediumCaution);
            return false;
        }

        if (!_powerCell.TryGetBatteryFromSlot(slot.Owner, out var batteryUid, out BatteryComponent? battery))
        {
            _popup.PopupEntity(Loc.GetString("power-cell-no-battery"), body, body, PopupType.MediumCaution);
            return false;
        }

        if (!_battery.TryUseCharge(batteryUid.Value, amount, battery))
        {
            _popup.PopupEntity(Loc.GetString("power-cell-insufficient"), body, body, PopupType.MediumCaution);
            return false;
        }

        return true;
    }

    private ContainerSlot EnsureStorageContainer(Entity<AugmentItemPanelComponent> ent)
    {
        return _containers.EnsureContainer<ContainerSlot>(ent.Owner, ent.Comp.StorageContainerId);
    }

    private bool EnsureStoredItem(Entity<AugmentItemPanelComponent> ent)
    {
        var storage = EnsureStorageContainer(ent);

        if (ent.Comp.SpawnedItem is { } existing && !Terminating(existing))
        {
            if (storage.ContainedEntity != existing && !_containers.Insert(existing, storage))
                return false;

            return true;
        }

        var spawnedItem = Spawn(ent.Comp.ItemPrototype, Transform(ent.Owner).Coordinates);
        ent.Comp.SpawnedItem = spawnedItem;
        Dirty(ent, ent.Comp);

        if (_containers.Insert(spawnedItem, storage))
            return true;

        QueueDel(spawnedItem);
        ent.Comp.SpawnedItem = null;
        Dirty(ent, ent.Comp);
        return false;
    }

    private void ApplyExtendHeldAnimation(AugmentItemPanelComponent component, EntityUid item)
    {
        if (string.IsNullOrEmpty(component.ExtendHeldPrefix))
            return;

        _item.SetHeldPrefix(item, component.ExtendHeldPrefix);
        if (component.ExtendHeldPrefixDuration <= TimeSpan.Zero)
        {
            _item.SetHeldPrefix(item, component.ExtendHeldPrefixAfter);
            return;
        }

        Timer.Spawn(component.ExtendHeldPrefixDuration, () =>
        {
            if (Deleted(item) || Terminating(item))
                return;

            _item.SetHeldPrefix(item, component.ExtendHeldPrefixAfter);
        });
    }

    private void StartActionCooldown(AugmentItemPanelComponent component)
    {
        if (component.ActionEntity is not { } action)
            return;

        if (component.ActionCooldown <= TimeSpan.Zero)
            return;

        _actions.StartUseDelay(action);
    }

    private void RemovePanelAction(AugmentItemPanelComponent component, EntityUid body)
    {
        if (!component.ActionEntity.HasValue)
            return;

        _actions.RemoveAction(body, component.ActionEntity.Value);
        component.ActionEntity = null;
    }

    private void TryRetractIfEquipped(Entity<AugmentItemPanelComponent> ent, EntityUid body)
    {
        if (ent.Comp.IsEquipped && ent.Comp.SpawnedItem.HasValue)
            RetractItem(ent, body);
    }

}

