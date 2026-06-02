using System.Diagnostics.CodeAnalysis;
using Content.Client._Onyx.UserInterface.Systems.Wiki;
using Content.Client._Onyx.Wiki.Components;
using Content.Client.Guidebook;
using Content.Client.Verbs;
using Content.Shared.Interaction;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Onyx.Wiki;

public sealed class WikiHelpSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IUserInterfaceManager _uiManager = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly WikiManager _wiki = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WikiHelpComponent, GetVerbsEvent<ExamineVerb>>(OnGetVerbs);
        SubscribeLocalEvent<WikiHelpComponent, ActivateInWorldEvent>(OnInteract);
    }

    private void OnGetVerbs(EntityUid uid, WikiHelpComponent component, GetVerbsEvent<ExamineVerb> args)
    {
        if (_tags.HasTag(uid, GuidebookSystem.GuideEmbedTag) || !TryGetFirstArticle(component, out var articleId))
            return;

        args.Verbs.Add(new()
        {
            Text = Loc.GetString("wiki-help-verb"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/information.svg.192dpi.png")),
            Act = () => OpenArticle(articleId),
            ClientExclusive = true,
            CloseMenu = true
        });
    }

    private void OnInteract(EntityUid uid, WikiHelpComponent component, ActivateInWorldEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (!component.OpenOnActivation
            || _tags.HasTag(uid, GuidebookSystem.GuideEmbedTag)
            || !TryGetFirstArticle(component, out var articleId))
            return;

        OpenArticle(articleId);
        args.Handled = true;
    }

    private bool TryGetFirstArticle(WikiHelpComponent component, [NotNullWhen(true)] out string? articleId)
    {
        foreach (var id in component.Articles)
        {
            if (!_wiki.HasArticlePrototype(id))
                continue;

            articleId = id;
            return true;
        }

        articleId = null;
        return false;
    }

    private void OpenArticle(string articleId)
    {
        _uiManager.GetUIController<WikiUIController>().OpenWiki(articleId);
    }
}
