using System.Numerics;
using Content.Shared._Onyx.Food.Components;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Item;
using Content.Shared.Localizations;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._Onyx.Food.Systems;

public sealed class PlateContainerSystem : EntitySystem
{
    private static readonly TimeSpan PopupCooldown = TimeSpan.FromSeconds(1);

    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlateContainerComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<PlateContainerComponent, AfterInteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<PlateContainerComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<PlateContainerComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAlternativeVerbs);
        SubscribeLocalEvent<PlateContainerComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
    }

    private void OnInit(Entity<PlateContainerComponent> ent, ref ComponentInit args)
    {
        var container = GetContainer(ent.Owner);
        container.ShowContents = false;
        container.OccludesLight = false;
    }

    private void OnInteractUsing(Entity<PlateContainerComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        var container = GetContainer(ent.Owner);
        var result = CanInsert(ent, args.Used, container);

        if (result != PlateInsertResult.Success)
        {
            TryShowInsertPopup(ent, args.User, result);
            args.Handled = true;
            return;
        }

        if (!_hands.TryDropIntoContainer(args.User, args.Used, container))
            return;

        ArrangeContents(ent.Owner, ent.Comp, container);
        args.Handled = true;
    }

    private void OnExamined(Entity<PlateContainerComponent> ent, ref ExaminedEvent args)
    {
        var container = GetContainer(ent.Owner);
        if (container.ContainedEntities.Count == 0)
        {
            args.PushMarkup(Loc.GetString("plate-container-examine-empty"));
            return;
        }

        var names = new List<string>(container.ContainedEntities.Count);
        foreach (var item in container.ContainedEntities)
        {
            var name = Identity.Name(item, EntityManager, args.Examiner);
            names.Add(FormattedMessage.EscapeText(name));
        }

        args.PushMarkup(Loc.GetString(
            "plate-container-examine-contents",
            ("items", ContentLocalizationManager.FormatList(names))));
    }

    private void TryShowInsertPopup(
        Entity<PlateContainerComponent> ent,
        EntityUid user,
        PlateInsertResult result)
    {
        var now = _timing.CurTime;
        if (ent.Comp.LastPopupUser == user && now < ent.Comp.NextPopupTime)
            return;

        ent.Comp.LastPopupUser = user;
        ent.Comp.NextPopupTime = now + PopupCooldown;
        _popup.PopupClient(Loc.GetString(GetPopup(result)), ent.Owner, user);
    }

    private void OnGetAlternativeVerbs(
        Entity<PlateContainerComponent> ent,
        ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var container = GetContainer(ent.Owner);
        if (container.ContainedEntities.Count == 0)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Act = () =>
            {
                if (container.ContainedEntities.Count == 0)
                    return;

                var item = container.ContainedEntities[^1];
                if (_hands.TryPickupAnyHand(user, item))
                    ArrangeContents(ent.Owner, ent.Comp, container);
            },
            Category = VerbCategory.Eject,
            Text = Loc.GetString("plate-container-verb-remove"),
            Priority = 1,
        });
    }

    private void OnInsertAttempt(Entity<PlateContainerComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Container.ID != PlateContainerComponent.ContainerId)
            return;

        if (CanInsert(ent, args.EntityUid, args.Container, args.AssumeEmpty) != PlateInsertResult.Success)
            args.Cancel();
    }

    public Container GetContainer(EntityUid plate)
    {
        return _container.EnsureContainer<Container>(plate, PlateContainerComponent.ContainerId);
    }

    private PlateInsertResult CanInsert(
        Entity<PlateContainerComponent> plate,
        EntityUid item,
        BaseContainer container,
        bool assumeEmpty = false)
    {
        if (item == plate.Owner ||
            HasComp<PlateContainerComponent>(item) ||
            HasComp<EntityStorageComponent>(item) ||
            HasComp<StorageComponent>(item) ||
            HasComp<UnremoveableComponent>(item) ||
            !TryComp<ItemComponent>(item, out var itemComp))
        {
            return PlateInsertResult.Invalid;
        }

        if (!assumeEmpty && container.ContainedEntities.Count >= plate.Comp.MaxItems)
            return PlateInsertResult.Full;

        if (_item.GetSizePrototype(itemComp.Size) > _item.GetSizePrototype(plate.Comp.MaxItemSize))
            return PlateInsertResult.TooLarge;

        if (!PassesFilters(plate.Comp, item))
            return PlateInsertResult.Invalid;

        return PlateInsertResult.Success;
    }

    private bool PassesFilters(PlateContainerComponent component, EntityUid item)
    {
        var prototype = MetaData(item).EntityPrototype?.ID;

        if (prototype != null && component.BlacklistPrototypes.Contains(prototype))
            return false;

        if (component.BlacklistTags.Count > 0 && _tag.HasAnyTag(item, component.BlacklistTags))
            return false;

        if (component.WhitelistPrototypes.Count == 0 && component.WhitelistTags.Count == 0)
            return true;

        var allowedPrototype = prototype != null &&
                               component.WhitelistPrototypes.Contains(prototype);
        var allowedTag = component.WhitelistTags.Count > 0 &&
                         _tag.HasAnyTag(item, component.WhitelistTags);

        return allowedPrototype || allowedTag;
    }

    private void ArrangeContents(EntityUid plate, PlateContainerComponent component, BaseContainer container)
    {
        for (var i = 0; i < container.ContainedEntities.Count; i++)
        {
            var offset = i < component.ItemOffsets.Count
                ? component.ItemOffsets[i]
                : Vector2.Zero;

            _transform.SetCoordinates(container.ContainedEntities[i], new EntityCoordinates(plate, offset));
        }
    }

    private static string GetPopup(PlateInsertResult result)
    {
        return result switch
        {
            PlateInsertResult.Full => "plate-container-full",
            PlateInsertResult.TooLarge => "plate-container-too-large",
            _ => "plate-container-invalid-item",
        };
    }

    private enum PlateInsertResult : byte
    {
        Success,
        Full,
        TooLarge,
        Invalid,
    }
}
