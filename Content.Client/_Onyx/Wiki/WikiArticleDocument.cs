using Content.Shared._Onyx.Wiki;

namespace Content.Client._Onyx.Wiki;

public sealed class WikiArticleDocument
{
    public WikiArticleDocument(WikiArticlePrototype prototype, string html, string plainText)
    {
        Prototype = prototype;
        Html = html;
        PlainText = plainText;
    }

    public WikiArticlePrototype Prototype { get; }
    public string Id => Prototype.ID;
    public string Title => Prototype.Title;
    public string Category => Prototype.Category;
    public string? ParentId => Prototype.Parent?.Id;
    public string Html { get; }
    public string PlainText { get; }
}
