using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared._Onyx.ProxyControl;
using Content.Shared.Administration;
using Content.Shared.NPC.Components;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Player;
using Content.Server._Onyx.NPC.UI;

namespace Content.Server._Onyx.NPC;

public sealed class NpcFactionDebugVerbSystem : EntitySystem
{
    [Dependency] private readonly IAdminManager _admin = default!;
    [Dependency] private readonly EuiManager _eui = default!;
    [Dependency] private readonly SharedProxyControlSystem _proxyControl = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NpcFactionMemberComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnGetVerbs(Entity<NpcFactionMemberComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        var actorUid = args.User;

        if (!TryComp<ActorComponent>(actorUid, out var actor) &&
            (!_proxyControl.TryGetControllerForTarget(args.User, ProxyControlRelayFlags.UserInterface, out actorUid) ||
             !TryComp(actorUid, out actor)))
            return;

        if (!_admin.HasAdminFlag(actor.PlayerSession, AdminFlags.Debug))
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("npc-faction-editor-verb"),
            Category = VerbCategory.Debug,
            Act = () => _eui.OpenEui(new NpcFactionEditorEui(ent.Owner), actor.PlayerSession),
        });
    }
}
