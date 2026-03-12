using Content.Goobstation.Shared.Augments;
using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Actions;
using Content.Shared.Body.Events;
using Content.Shared.CartridgeLoader;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.MedicalScanner;
using Content.Shared.Mobs.Components;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Whitelist;
using Content.Server.CartridgeLoader;
using Content.Server.CartridgeLoader.Cartridges;
using Content.Server.Medical.Components;
using Content.Server.PowerCell;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Utility;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentHoloPdaSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly AugmentSystem _augment = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;

    private const float IdEjectDelay = 3f;
    private const float CartridgeEjectDelay = 2f;
    private const string MedTekScanAction = "ActionAugmentHoloPdaMedTekScan";
    private static readonly SpriteSpecifier MedTekActionIcon =
        new SpriteSpecifier.Rsi(new ResPath("/Textures/_CorvaxGoob/Interface/Misc/program_icons.rsi"), "medtek");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentHoloPdaComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<AugmentHoloPdaComponent, OrganAddedToBodyEvent>(OnOrganAdded);
        SubscribeLocalEvent<AugmentHoloPdaComponent, OrganRemovedFromBodyEvent>(OnOrganRemoved);
        SubscribeLocalEvent<AugmentHoloPdaComponent, AugmentHoloPdaOpenEvent>(OnOpenAction);
        SubscribeLocalEvent<AugmentHoloPdaComponent, AugmentHoloPdaMedTekScanEvent>(OnMedTekScanAction);
        SubscribeLocalEvent<AugmentHoloPdaComponent, AugmentEmpDisabledEvent>(OnEmpDisabled);
        SubscribeLocalEvent<AugmentHoloPdaComponent, AugmentManuallyDisabledEvent>(OnManuallyDisabled);

        SubscribeLocalEvent<AugmentHoloPdaComponent, AugmentHoloPdaEjectIdDoAfterEvent>(OnEjectIdDoAfter);
        SubscribeLocalEvent<AugmentHoloPdaComponent, AugmentHoloPdaEjectCartridgeDoAfterEvent>(OnEjectCartridgeDoAfter);
        SubscribeLocalEvent<AugmentHoloPdaComponent, AugmentHoloPdaReplaceIdDoAfterEvent>(OnReplaceIdDoAfter);

        SubscribeLocalEvent<AugmentHoloPdaComponent, EntInsertedIntoContainerMessage>(OnAugmentItemInserted);
        SubscribeLocalEvent<AugmentHoloPdaComponent, EntRemovedFromContainerMessage>(OnAugmentItemRemoved);

        SubscribeLocalEvent<InstalledAugmentsComponent, EntInsertedIntoContainerMessage>(OnBodyItemInserted);
        SubscribeLocalEvent<InstalledAugmentsComponent, ItemSlotInsertAttemptEvent>(OnBodySlotInsertAttempt);
        SubscribeLocalEvent<InstalledAugmentsComponent, ItemSlotEjectAttemptEvent>(OnBodyEjectAttempt);
        SubscribeLocalEvent<InstalledAugmentsComponent, GetVerbsEvent<AlternativeVerb>>(OnBodyGetAlternativeVerbs);

        SubscribeLocalEvent<AugmentHoloPdaComponent, ItemSlotInsertAttemptEvent>(OnAugmentSlotInsertAttempt);
    }

    #region Lifecycle

    private void OnComponentInit(Entity<AugmentHoloPdaComponent> ent, ref ComponentInit args)
    {
        ent.Comp.IdSlot.Whitelist = new EntityWhitelist { Components = new[] { "IdCard" } };
        ent.Comp.IdSlot.Name = "access-id-card-component-default";
        _itemSlots.AddItemSlot(ent, AugmentHoloPdaComponent.HoloPdaIdSlotId, ent.Comp.IdSlot);
    }

    private void OnOrganAdded(Entity<AugmentHoloPdaComponent> ent, ref OrganAddedToBodyEvent args)
    {
        _actions.AddAction(args.Body, ref ent.Comp.ActionEntity, "ActionAugmentHoloPda", ent);

        if (TryComp<PdaComponent>(ent, out var pda))
            pda.PdaOwner = args.Body;

        ent.Comp.BodyIdSlot.Whitelist = new EntityWhitelist { Components = new[] { "IdCard" } };
        ent.Comp.BodyIdSlot.Name = "access-id-card-component-default";
        _itemSlots.AddItemSlot(args.Body, AugmentHoloPdaComponent.HoloPdaBodyIdSlotId, ent.Comp.BodyIdSlot);

        ent.Comp.CartridgeSlot.Whitelist = new EntityWhitelist { Components = new[] { "Cartridge" } };
        ent.Comp.CartridgeSlot.Name = "device-pda-slot-component-slot-name-cartridge";
        _itemSlots.AddItemSlot(args.Body, AugmentHoloPdaComponent.HoloPdaCartridgeSlotId, ent.Comp.CartridgeSlot);

        UpdateMedTekScanAction(ent, args.Body);
    }

    private void OnOrganRemoved(Entity<AugmentHoloPdaComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        if (ent.Comp.ActionEntity.HasValue)
        {
            _actions.RemoveAction(args.OldBody, ent.Comp.ActionEntity.Value);
            ent.Comp.ActionEntity = null;
        }

        _ui.CloseUi(ent.Owner, PdaUiKey.Key);

        if (TryComp<PdaComponent>(ent, out var pda))
        {
            pda.PdaOwner = null;
            pda.ContainedId = null;
        }

        _itemSlots.RemoveItemSlot(args.OldBody, ent.Comp.BodyIdSlot);
        _itemSlots.RemoveItemSlot(args.OldBody, ent.Comp.CartridgeSlot);
        RemoveMedTekScanAction(ent, args.OldBody);
    }

    #endregion

    #region UI

    private void OnOpenAction(Entity<AugmentHoloPdaComponent> ent, ref AugmentHoloPdaOpenEvent args)
    {
        if (args.Handled)
            return;

        if (!TryGetOwningBody(ent, args.Performer, out _))
            return;

        if (!_ui.HasUi(ent.Owner, PdaUiKey.Key))
            return;

        if (!CanUseHoloPda(ent, args.Performer))
            return;

        _ui.TryToggleUi(ent.Owner, PdaUiKey.Key, args.Performer);
        args.Handled = true;
    }

    private void OnMedTekScanAction(Entity<AugmentHoloPdaComponent> ent, ref AugmentHoloPdaMedTekScanEvent args)
    {
        if (args.Handled)
            return;

        if (!TryGetOwningBody(ent, args.Performer, out var body))
            return;

        if (!CanUseHoloPda(ent, body))
            return;

        if (!TryComp<HealthAnalyzerComponent>(ent, out var analyzer)
            || !_cartridgeLoader.HasProgram<MedTekCartridgeComponent>(ent))
            return;

        if (!HasComp<MobStateComponent>(args.Target))
            return;

        if (!_powerCell.HasDrawCharge(ent, user: args.Performer))
            return;

        if (analyzer.MaxScanRange is { } range &&
            !_interaction.InRangeUnobstructed(args.Performer, args.Target, range))
            return;

        _audio.PlayPvs(analyzer.ScanningBeginSound, ent);

        var started = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
            args.Performer,
            analyzer.ScanDelay,
            new HealthAnalyzerDoAfterEvent(),
            ent,
            target: args.Target,
            used: ent)
        {
            BreakOnMove = true,
        });

        if (!started)
            return;

        args.Handled = true;

        if (args.Target == args.Performer || analyzer.Silent)
            return;

        var msg = Loc.GetString("health-analyzer-popup-scan-target", ("user", Identity.Entity(args.Performer, EntityManager)));
        _popup.PopupEntity(msg, args.Target, args.Target, PopupType.Medium);
    }

    private void OnEmpDisabled(Entity<AugmentHoloPdaComponent> ent, ref AugmentEmpDisabledEvent args)
    {
        _ui.CloseUi(ent.Owner, PdaUiKey.Key);
    }

    private void OnManuallyDisabled(Entity<AugmentHoloPdaComponent> ent, ref AugmentManuallyDisabledEvent args)
    {
        _ui.CloseUi(ent.Owner, PdaUiKey.Key);
    }

    #endregion

    #region Container Sync

    private void OnBodyItemInserted(Entity<InstalledAugmentsComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        var augment = FindHoloPdaAugment(ent);
        if (augment == null)
            return;

        if (args.Container.ID == AugmentHoloPdaComponent.HoloPdaBodyIdSlotId)
        {
            if (_containers.TryGetContainer(augment.Value, AugmentHoloPdaComponent.HoloPdaIdSlotId, out var idContainer))
            {
                _containers.Remove(args.Entity, args.Container, force: true);
                _containers.Insert(args.Entity, idContainer);
            }
        }
        else if (args.Container.ID == AugmentHoloPdaComponent.HoloPdaCartridgeSlotId)
        {
            if (TryComp<CartridgeLoaderComponent>(augment.Value, out var loader)
                && loader.CartridgeSlot.ContainerSlot is { } cartridgeContainer)
            {
                _containers.Remove(args.Entity, args.Container, force: true);
                _containers.Insert(args.Entity, cartridgeContainer);
            }
        }
    }

    private void OnAugmentItemInserted(Entity<AugmentHoloPdaComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == AugmentHoloPdaComponent.HoloPdaIdSlotId
            && TryComp<PdaComponent>(ent, out var pda))
        {
            pda.ContainedId = args.Entity;
        }

        if (args.Container.ID == CartridgeLoaderComponent.CartridgeSlotId)
        {
            var body = _augment.GetBody(ent);
            if (body != null)
                UpdateMedTekScanAction(ent, body.Value);
        }
    }

    private void OnAugmentItemRemoved(Entity<AugmentHoloPdaComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == AugmentHoloPdaComponent.HoloPdaIdSlotId)
        {
            if (TryComp<PdaComponent>(ent, out var pda))
                pda.ContainedId = null;
        }

        if (args.Container.ID == CartridgeLoaderComponent.CartridgeSlotId)
        {
            var body = _augment.GetBody(ent);
            if (body != null)
            {
                _hands.TryPickupAnyHand(body.Value, args.Entity);
                UpdateMedTekScanAction(ent, body.Value);
            }
        }
    }

    #endregion

    #region Verbs & Eject

    private void OnBodySlotInsertAttempt(Entity<InstalledAugmentsComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.Cancelled || args.Slot.ID != AugmentHoloPdaComponent.HoloPdaBodyIdSlotId)
            return;

        var augment = FindHoloPdaAugment(ent);
        if (augment == null)
            return;

        if (!_containers.TryGetContainer(augment.Value, AugmentHoloPdaComponent.HoloPdaIdSlotId, out var idContainer)
            || idContainer.ContainedEntities.Count == 0)
            return;

        args.Cancelled = true;

        if (args.User == null)
            return;

        var user = args.User.Value;
        var body = ent.Owner;

        if (user == body)
        {
            ReplaceId(augment.Value, args.Item, user);
            return;
        }

        _popup.PopupEntity(Loc.GetString("augment-holopda-eject-popup"), body, body, PopupType.MediumCaution);

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
            user,
            IdEjectDelay,
            new AugmentHoloPdaReplaceIdDoAfterEvent { NewIdCard = GetNetEntity(args.Item) },
            augment.Value,
            target: body)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private void OnBodyEjectAttempt(Entity<InstalledAugmentsComponent> ent, ref ItemSlotEjectAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (args.Slot.ID is AugmentHoloPdaComponent.HoloPdaBodyIdSlotId
            or AugmentHoloPdaComponent.HoloPdaCartridgeSlotId)
        {
            args.Cancelled = true;
        }
    }

    private void OnBodyGetAlternativeVerbs(Entity<InstalledAugmentsComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var augment = FindHoloPdaAugment(ent);
        if (augment == null)
            return;

        var user = args.User;
        var body = ent.Owner;
        var augUid = augment.Value;
        var comp = Comp<AugmentHoloPdaComponent>(augUid);
        var isOwner = user == body;

        if (_containers.TryGetContainer(augUid, AugmentHoloPdaComponent.HoloPdaIdSlotId, out var idCont)
            && idCont.ContainedEntities.Count > 0)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("augment-holopda-eject-id-verb"),
                Priority = -1,
                Act = () =>
                {
                    if (isOwner)
                    {
                        ForceEjectId(augUid, user);
                        return;
                    }

                    _popup.PopupEntity(Loc.GetString("augment-holopda-eject-popup"), body, body, PopupType.MediumCaution);
                    _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
                        user, IdEjectDelay, new AugmentHoloPdaEjectIdDoAfterEvent(), augUid, target: body)
                    {
                        BreakOnMove = true,
                        BreakOnDamage = true,
                    });
                }
            });
        }

        if (TryComp<CartridgeLoaderComponent>(augUid, out var loader)
            && loader.CartridgeSlot.ContainerSlot?.ContainedEntity is { } cartridge)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("augment-holopda-eject-cartridge-verb"),
                IconEntity = GetNetEntity(cartridge),
                Priority = -2,
                Act = () =>
                {
                    _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
                        user, CartridgeEjectDelay, new AugmentHoloPdaEjectCartridgeDoAfterEvent(), augUid, target: body)
                    {
                        BreakOnMove = true,
                        BreakOnDamage = true,
                    });
                }
            });
        }
    }

    private void OnEjectIdDoAfter(Entity<AugmentHoloPdaComponent> ent, ref AugmentHoloPdaEjectIdDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;
        ForceEjectId(ent, args.User);
    }

    private void OnEjectCartridgeDoAfter(Entity<AugmentHoloPdaComponent> ent, ref AugmentHoloPdaEjectCartridgeDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;

        if (TryComp<CartridgeLoaderComponent>(ent, out var loader)
            && loader.CartridgeSlot.ContainerSlot?.ContainedEntity is { } cartridge)
        {
            _containers.Remove(cartridge, loader.CartridgeSlot.ContainerSlot, force: true);
            _audio.PlayPvs(ent.Comp.EjectSound, args.User);
        }
    }

    private void OnReplaceIdDoAfter(Entity<AugmentHoloPdaComponent> ent, ref AugmentHoloPdaReplaceIdDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;

        var newId = GetEntity(args.NewIdCard);
        if (Exists(newId))
            ReplaceId(ent, newId, args.User);
    }

    private void ReplaceId(EntityUid augUid, EntityUid newId, EntityUid user)
    {
        if (!_containers.TryGetContainer(augUid, AugmentHoloPdaComponent.HoloPdaIdSlotId, out var idContainer))
            return;

        if (idContainer.ContainedEntities.Count > 0)
        {
            var oldId = idContainer.ContainedEntities[0];
            _containers.Remove(oldId, idContainer, force: true);
            _hands.TryPickupAnyHand(user, oldId);
        }

        _containers.Insert(newId, idContainer);

        if (TryComp<AugmentHoloPdaComponent>(augUid, out var comp))
            _audio.PlayPvs(comp.EjectSound, user);
    }

    private void ForceEjectId(EntityUid augUid, EntityUid user)
    {
        if (!_containers.TryGetContainer(augUid, AugmentHoloPdaComponent.HoloPdaIdSlotId, out var idContainer)
            || idContainer.ContainedEntities.Count == 0)
            return;

        var contained = idContainer.ContainedEntities[0];
        _containers.Remove(contained, idContainer, force: true);
        _hands.TryPickupAnyHand(user, contained);

        if (TryComp<AugmentHoloPdaComponent>(augUid, out var comp))
            _audio.PlayPvs(comp.EjectSound, user);
    }

    #endregion

    #region Block PDA Features

    private void OnAugmentSlotInsertAttempt(Entity<AugmentHoloPdaComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (args.Slot.ID is PdaComponent.PdaIdSlotId
            or PdaComponent.PdaPenSlotId
            or PdaComponent.PdaPaiSlotId)
        {
            args.Cancelled = true;
        }
    }

    #endregion

    #region Helpers

    private bool CanUseHoloPda(Entity<AugmentHoloPdaComponent> ent, EntityUid body)
    {
        if (TryGetBlockedPopupLocKey(ent.Owner, out var popupLocKey))
        {
            _popup.PopupEntity(Loc.GetString(popupLocKey), body, body, PopupType.SmallCaution);
            return false;
        }

        return true;
    }

    private bool TryGetOwningBody(Entity<AugmentHoloPdaComponent> ent, EntityUid performer, out EntityUid body)
    {
        if (_augment.GetBody(ent) is { } ownerBody && ownerBody == performer)
        {
            body = ownerBody;
            return true;
        }

        body = default;
        return false;
    }

    private bool TryGetBlockedPopupLocKey(EntityUid augment, out string popupLocKey)
    {
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

        if (HasComp<AugmentBrainDeactivatedComponent>(augment))
        {
            popupLocKey = "augment-brain-disabled";
            return true;
        }

        if (HasComp<AugmentNeuroManuallyDisabledComponent>(augment))
        {
            popupLocKey = "augment-disabled-manually";
            return true;
        }

        popupLocKey = string.Empty;
        return false;
    }

    private void UpdateMedTekScanAction(Entity<AugmentHoloPdaComponent> ent, EntityUid body)
    {
        if (_cartridgeLoader.HasProgram<MedTekCartridgeComponent>(ent))
        {
            _actions.AddAction(body, ref ent.Comp.MedTekScanActionEntity, MedTekScanAction, ent);
            if (ent.Comp.MedTekScanActionEntity is { } action)
                _actions.SetIcon(action, MedTekActionIcon);
            return;
        }

        RemoveMedTekScanAction(ent, body);
    }

    private void RemoveMedTekScanAction(Entity<AugmentHoloPdaComponent> ent, EntityUid body)
    {
        if (ent.Comp.MedTekScanActionEntity is not { } action)
            return;

        _actions.RemoveAction(body, action);
        ent.Comp.MedTekScanActionEntity = null;
    }

    private EntityUid? FindHoloPdaAugment(Entity<InstalledAugmentsComponent> ent)
    {
        foreach (var netEnt in ent.Comp.InstalledAugments)
        {
            var aug = GetEntity(netEnt);
            if (HasComp<AugmentHoloPdaComponent>(aug))
                return aug;
        }
        return null;
    }

    #endregion
}
