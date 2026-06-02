using Content.Client._Onyx.UserInterface.Systems.Wiki;
using Content.Shared.Administration;
using Robust.Client.UserInterface;
using Robust.Shared.Console;

namespace Content.Client._Onyx.Wiki;

[AnyCommand]
public sealed class WikiCommand : IConsoleCommand
{
    [Dependency] private readonly IUserInterfaceManager _uiManager = default!;
    [Dependency] private readonly WikiManager _wiki = default!;

    public string Command => "wiki";
    public string Description => "Opens the wiki window.";
    public string Help => "Usage: wiki [articleId]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 1)
        {
            shell.WriteLine(Help);
            return;
        }

        var articleId = args.Length == 1 ? args[0] : null;
        if (articleId != null && !_wiki.HasArticlePrototype(articleId))
        {
            shell.WriteError($"Unknown wiki article: {articleId}");
            return;
        }

        _uiManager.GetUIController<WikiUIController>().OpenWiki(articleId);
    }
}
