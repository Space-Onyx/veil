using System.Linq;
using Content.Shared._Onyx.Augments;
using Content.Shared.Actions;
using Content.Shared.Body.Events;
using Content.Shared.Body.Part;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Goobstation.Shared.Augments;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Onyx.Augments;

public sealed class AugmentItemPanelSystem : SharedAugmentItemPanelSystem
{
    [Dependency] private readonly AugmentSystem _augment = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

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

        if (ent.Comp.ActionEntity.HasValue && ent.Comp.Icon != null)
        {
            _actions.SetIcon(ent.Comp.ActionEntity.Value, ent.Comp.Icon);
        }
    }

    private void OnToggleItem(Entity<AugmentItemPanelComponent> ent, ref AugmentActionEvent args)
    {
        if (_augment.GetBody(ent) is not {} body)
            return;

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

            var activeComp = EnsureComp<AugmentItemPanelActiveItemComponent>(item);
            activeComp.AugmentEntity = ent;
        }

        var spawnedItem = ent.Comp.SpawnedItem.Value;

        if (!_hands.TryPickup(body, spawnedItem, hand))
        {
            _popup.PopupEntity(Loc.GetString("augment-item-panel-cannot-equip"), body, body, PopupType.SmallCaution);
            return;
        }

        ent.Comp.IsEquipped = true;
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
        ent.Comp.IsEquipped = false;

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
}
