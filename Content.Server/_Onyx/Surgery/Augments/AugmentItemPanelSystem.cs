using System.Linq;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Actions;
using Content.Shared.Body.Events;
using Content.Shared.Body.Part;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Components;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Popups;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Goobstation.Shared.Augments;
using Content.Shared.Item;
using Robust.Shared.Audio.Systems;
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

    private EntityQuery<HandsComponent> _handsQuery;
    private EntityQuery<BodyPartComponent> _partQuery;

    public const string DefaultActionPrototype = "ActionAugmentToggleItemPanel";

    public override void Initialize()
    {
        base.Initialize();

        _handsQuery = GetEntityQuery<HandsComponent>();
        _partQuery = GetEntityQuery<BodyPartComponent>();

        SubscribeLocalEvent<AugmentItemPanelComponent, OrganAddedToBodyEvent>(OnOrganAddedToBody);
        SubscribeLocalEvent<AugmentItemPanelComponent, OrganRemovedFromBodyEvent>(OnOrganRemovedFromBody);
        SubscribeLocalEvent<AugmentItemPanelComponent, AugmentActionEvent>(OnToggleItem);
        SubscribeLocalEvent<AugmentItemPanelComponent, AugmentEmpDisabledEvent>(OnEmpDisabled);
        SubscribeLocalEvent<AugmentItemPanelComponent, AugmentLostPowerEvent>(OnLostPower);
    }

    private void OnOrganAddedToBody(Entity<AugmentItemPanelComponent> ent, ref OrganAddedToBodyEvent args)
    {
        AddActionWithItemIcon(ent, args.Body);
    }

    private void OnOrganRemovedFromBody(Entity<AugmentItemPanelComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        if (ent.Comp.ActionEntity.HasValue)
        {
            _actions.RemoveAction(args.OldBody, ent.Comp.ActionEntity.Value);
            ent.Comp.ActionEntity = null;
        }

        if (ent.Comp.IsEquipped && ent.Comp.SpawnedItem.HasValue)
        {
            RetractItem(ent, args.OldBody);
        }

        if (ent.Comp.SpawnedItem.HasValue && !Terminating(ent.Comp.SpawnedItem.Value))
        {
            QueueDel(ent.Comp.SpawnedItem.Value);
            ent.Comp.SpawnedItem = null;
        }
    }

    private void AddActionWithItemIcon(Entity<AugmentItemPanelComponent> ent, EntityUid body)
    {
        _actions.AddAction(body, ref ent.Comp.ActionEntity, DefaultActionPrototype, ent);

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

    private void OnEmpDisabled(Entity<AugmentItemPanelComponent> ent, ref AugmentEmpDisabledEvent args)
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
        if (_augment.GetBody(ent) is not {} body)
            return;

        if (HasComp<AugmentEmpDisabledComponent>(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("augment-emp-disabled"), body, body, PopupType.SmallCaution);
            return;
        }

        if (RequiresPower(ent) &&
            !TryUseChargeBody(body, ent.Comp.PowerCost))
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

    private void DeployItem(Entity<AugmentItemPanelComponent> ent, EntityUid body)
    {
        if (!_handsQuery.TryComp(body, out var handsComp))
            return;

        var hand = GetHandForAugment(ent, body, handsComp);
        if (hand == null)
        {
            _popup.PopupEntity(Loc.GetString("augment-item-panel-no-hand"), body, body, PopupType.LargeCaution);
            return;
        }

        if (_hands.GetHeldItem(body, hand) != null)
        {
            _popup.PopupEntity(Loc.GetString("augment-item-panel-hand-full"), body, body, PopupType.SmallCaution);
            return;
        }

        if (!ent.Comp.SpawnedItem.HasValue || Terminating(ent.Comp.SpawnedItem.Value))
        {
            var item = Spawn(ent.Comp.ItemPrototype, Transform(ent).Coordinates);
            ent.Comp.SpawnedItem = item;
            EnsureComp<UnremoveableComponent>(item);
        }

        var spawnedItem = ent.Comp.SpawnedItem.Value;

        if (!_hands.TryPickup(body, spawnedItem, hand))
        {
            _popup.PopupEntity(Loc.GetString("augment-item-panel-cannot-equip"), body, body, PopupType.SmallCaution);
            return;
        }

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

        if (_hands.IsHolding(body, item, out _))
        {
            _hands.TryDrop(body, item);
        }

        Transform(item).Coordinates = Transform(ent).Coordinates;
        if (ent.Comp.RetractSound != null)
            _audio.PlayPvs(ent.Comp.RetractSound, body);

        ent.Comp.IsEquipped = false;
        _toggle.TryDeactivate(ent.Owner);
        StartActionCooldown(ent.Comp);

        _popup.PopupEntity(Loc.GetString("augment-item-panel-retracted", ("item", item)), body, body);
    }

    private string? GetHandForAugment(Entity<AugmentItemPanelComponent> ent, EntityUid body, HandsComponent handsComp)
    {
        var partUid = Transform(ent).ParentUid;
        if (!_partQuery.TryComp(partUid, out var part))
            return handsComp.Hands.Keys.FirstOrDefault();

        var location = part.Symmetry switch
        {
            BodyPartSymmetry.None => HandLocation.Middle,
            BodyPartSymmetry.Left => HandLocation.Left,
            BodyPartSymmetry.Right => HandLocation.Right,
            _ => HandLocation.Middle,
        };

        foreach (var (hand, handLocation) in handsComp.Hands)
        {
            if (handLocation.Location == location)
                return hand;
        }

        return handsComp.Hands.Keys.FirstOrDefault();
    }

    private bool RequiresPower(Entity<AugmentItemPanelComponent> ent)
    {
        return ent.Comp.RequiresPower
            && ent.Comp.PowerCost > 0f
            && (!TryComp<AugmentPowerConfigComponent>(ent.Owner, out var globalConfig) || globalConfig.RequiresPower);
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
}
