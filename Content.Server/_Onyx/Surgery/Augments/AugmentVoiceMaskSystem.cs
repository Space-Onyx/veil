using Content.Shared._Onyx.Surgery.Augments;
using Content.Shared.Body.Events;
using Content.Shared.Chat;
using Content.Shared.VoiceMask;
using Content.Shared.Speech;
using Content.Shared.Actions;
using Content.Shared.Popups;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Goobstation.Shared.Augments;
using Content.Shared.ADT.SpeechBarks;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._Onyx.Surgery.Augments;

public sealed class AugmentVoiceMaskSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly AugmentSystem _augment = default!;

    private int _maxNameLength;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AugmentVoiceMaskComponent, OrganAddedToBodyEvent>(OnOrganAddedToBody);
        SubscribeLocalEvent<AugmentVoiceMaskComponent, OrganRemovedFromBodyEvent>(OnOrganRemovedFromBody);
        SubscribeLocalEvent<AugmentVoiceMaskComponent, TransformSpeakerNameEvent>(OnTransformSpeakerName);
        SubscribeLocalEvent<AugmentVoiceMaskComponent, TransformSpeakerBarkEvent>(OnTransformSpeakerBark);
        SubscribeLocalEvent<AugmentVoiceMaskComponent, VoiceMaskSetNameEvent>(OnSetNameEvent);
        SubscribeLocalEvent<AugmentVoiceMaskComponent, AugmentEmpDisabledEvent>(OnEmpDisabled);
        SubscribeLocalEvent<AugmentVoiceMaskComponent, AugmentManuallyDisabledEvent>(OnManuallyDisabled);

        Subs.BuiEvents<AugmentVoiceMaskComponent>(VoiceMaskUIKey.Key, subs =>
        {
            subs.Event<VoiceMaskChangeNameMessage>(OnChangeName);
            subs.Event<VoiceMaskChangeVerbMessage>(OnChangeVerb);
            subs.Event<VoiceMaskChangeBarkMessage>(OnChangeBark);
            subs.Event<VoiceMaskChangeBarkPitchMessage>(OnChangeBarkPitch);
        });

        Subs.CVar(_cfg, CCVars.MaxNameLength, value => _maxNameLength = value, true);
    }

    private void OnOrganAddedToBody(Entity<AugmentVoiceMaskComponent> ent, ref OrganAddedToBodyEvent args)
    {
        _actions.AddAction(args.Body, ref ent.Comp.ActionEntity, "ActionAugmentVoiceMask", ent);
    }

    private void OnOrganRemovedFromBody(Entity<AugmentVoiceMaskComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        if (ent.Comp.ActionEntity.HasValue)
        {
            _actions.RemoveAction(args.OldBody, ent.Comp.ActionEntity.Value);
            ent.Comp.ActionEntity = null;
        }

        _uiSystem.CloseUi(ent.Owner, VoiceMaskUIKey.Key);
    }

    private void OnEmpDisabled(Entity<AugmentVoiceMaskComponent> ent, ref AugmentEmpDisabledEvent args)
    {
        _uiSystem.CloseUi(ent.Owner, VoiceMaskUIKey.Key);
    }

    private void OnManuallyDisabled(Entity<AugmentVoiceMaskComponent> ent, ref AugmentManuallyDisabledEvent args)
    {
        _uiSystem.CloseUi(ent.Owner, VoiceMaskUIKey.Key);
    }

    private void OnTransformSpeakerName(Entity<AugmentVoiceMaskComponent> ent, ref TransformSpeakerNameEvent args)
    {
        if (_augment.GetBody(ent) != args.Sender)
            return;

        if (HasComp<AugmentSuppressedByProjectorsComponent>(ent.Owner))
            return;

        if (HasComp<AugmentEmpDisabledComponent>(ent.Owner))
            return;

        if (HasComp<AugmentBrainDeactivatedComponent>(ent.Owner))
            return;

        if (HasComp<AugmentNeuroManuallyDisabledComponent>(ent.Owner))
            return;

        args.VoiceName = GetCurrentVoiceName(ent);
        args.SpeechVerb = ent.Comp.VoiceMaskSpeechVerb ?? args.SpeechVerb;
    }

    private void OnTransformSpeakerBark(Entity<AugmentVoiceMaskComponent> ent, ref TransformSpeakerBarkEvent args)
    {
        if (_augment.GetBody(ent) != args.Sender)
            return;

        if (HasComp<AugmentSuppressedByProjectorsComponent>(ent.Owner))
            return;

        if (HasComp<AugmentEmpDisabledComponent>(ent.Owner))
            return;

        if (HasComp<AugmentBrainDeactivatedComponent>(ent.Owner))
            return;

        if (HasComp<AugmentNeuroManuallyDisabledComponent>(ent.Owner))
            return;

        if (_proto.TryIndex<BarkPrototype>(ent.Comp.BarkId, out var proto))
        {
            args.Data.Pitch = Math.Clamp(ent.Comp.BarkPitch, 0.5f, 2.0f);
            args.Data.Sound = proto.Sound;
        }
    }

    private void OnSetNameEvent(Entity<AugmentVoiceMaskComponent> ent, ref VoiceMaskSetNameEvent args)
    {
        if (_augment.GetBody(ent) != args.Performer)
            return;

        if (HasComp<AugmentSuppressedByProjectorsComponent>(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("augment-suppression-disabled"), args.Performer, args.Performer, PopupType.SmallCaution);
            return;
        }

        if (HasComp<AugmentEmpDisabledComponent>(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("augment-emp-disabled"), args.Performer, args.Performer, PopupType.SmallCaution);
            return;
        }

        if (HasComp<AugmentBrainDeactivatedComponent>(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("augment-brain-disabled"), args.Performer, args.Performer, PopupType.SmallCaution);
            return;
        }

        if (HasComp<AugmentNeuroManuallyDisabledComponent>(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("augment-disabled-manually"), args.Performer, args.Performer, PopupType.SmallCaution);
            return;
        }

        if (!_uiSystem.HasUi(ent.Owner, VoiceMaskUIKey.Key))
            return;

        _uiSystem.OpenUi(ent.Owner, VoiceMaskUIKey.Key, args.Performer);
        UpdateUI(ent);
    }

    #region UI Message Handlers

    private void OnChangeName(Entity<AugmentVoiceMaskComponent> ent, ref VoiceMaskChangeNameMessage message)
    {
        if (message.Name.Length > _maxNameLength || message.Name.Length <= 0)
        {
            _popup.PopupEntity(Loc.GetString("voice-mask-popup-failure"), ent, message.Actor, PopupType.SmallCaution);
            return;
        }

        ent.Comp.VoiceMaskName = message.Name;
        _adminLogger.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(message.Actor):player} set voice of augment {ToPrettyString(ent):augment}: {ent.Comp.VoiceMaskName}");

        _popup.PopupEntity(Loc.GetString("voice-mask-popup-success"), ent, message.Actor);
        UpdateUI(ent);
    }

    private void OnChangeVerb(Entity<AugmentVoiceMaskComponent> ent, ref VoiceMaskChangeVerbMessage msg)
    {
        if (msg.Verb is { } id && !_proto.HasIndex<SpeechVerbPrototype>(id))
            return;

        ent.Comp.VoiceMaskSpeechVerb = msg.Verb;
        _popup.PopupEntity(Loc.GetString("voice-mask-popup-success"), ent, msg.Actor);
        UpdateUI(ent);
    }

    private void OnChangeBark(Entity<AugmentVoiceMaskComponent> ent, ref VoiceMaskChangeBarkMessage msg)
    {
        ent.Comp.BarkId = msg.Proto;
        _popup.PopupEntity(Loc.GetString("voice-mask-popup-success"), ent, msg.Actor);
        UpdateUI(ent);
    }

    private void OnChangeBarkPitch(Entity<AugmentVoiceMaskComponent> ent, ref VoiceMaskChangeBarkPitchMessage msg)
    {
        if (float.TryParse(msg.Pitch, out var pitch))
            ent.Comp.BarkPitch = pitch;

        _popup.PopupEntity(Loc.GetString("voice-mask-popup-success"), ent, msg.Actor);
        UpdateUI(ent);
    }

    #endregion

    private void UpdateUI(Entity<AugmentVoiceMaskComponent> ent)
    {
        if (!_uiSystem.HasUi(ent.Owner, VoiceMaskUIKey.Key))
            return;

        _uiSystem.SetUiState(ent.Owner, VoiceMaskUIKey.Key, new VoiceMaskBuiState(
            GetCurrentVoiceName(ent),
            ent.Comp.VoiceId,
            ent.Comp.VoiceMaskSpeechVerb,
            ent.Comp.BarkId,
            ent.Comp.BarkPitch,
            null));
    }

    private string GetCurrentVoiceName(Entity<AugmentVoiceMaskComponent> ent)
    {
        return ent.Comp.VoiceMaskName ?? Loc.GetString("voice-mask-default-name-override");
    }
}
