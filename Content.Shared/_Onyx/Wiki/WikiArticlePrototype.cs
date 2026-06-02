using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Onyx.Wiki;

[Prototype]
public sealed partial class WikiArticlePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Title = string.Empty;

    /// <summary>
    /// Top-level group shown in the wiki navigation tree.
    /// </summary>
    [DataField]
    public string Category = "General";

    /// <summary>
    /// Optional parent article id. Used only for client-side navigation tree nesting.
    /// </summary>
    [DataField]
    public ProtoId<WikiArticlePrototype>? Parent;

    /// <summary>
    /// Article HTML fragment under Resources/Wiki/Articles.
    /// </summary>
    [DataField(required: true)]
    public ResPath File = default!;

    /// <summary>
    /// Search keywords that are not necessarily visible in the article body.
    /// </summary>
    [DataField]
    public List<string> Tags = new();

    /// <summary>
    /// Alternative article names used by search.
    /// </summary>
    [DataField]
    public List<string> Aliases = new();

    /// <summary>
    /// Sort order inside a category or parent article.
    /// </summary>
    [DataField]
    public int Priority;
}
