using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Content.Shared._Onyx.Wiki;
using Robust.Client.WebView;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Onyx.Wiki;

public sealed class WikiManager
{
    public const int MaxArticleBytes = 256 * 1024;
    public const int MaxTotalArticleBytes = 8 * 1024 * 1024;
    public const int MaxArticles = 512;
    public const int MaxImageBytes = 2 * 1024 * 1024;
    public const int MaxArticleTreeDepth = 64;

    private const int MinArticleSearchLength = 2;
    private const int MaxArticleSearchLength = 64;
    private const int MaxArticleSearchHighlights = 100;
    private const string ArticleRoot = "/Wiki/Articles/";
    private const string StyleRoot = "/Wiki/Styles/";
    private const string ImageRoot = "/Wiki/Images/";

    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IResourceManager _resources = default!;

    private readonly Dictionary<string, WikiArticleDocument> _articles = new();
    private readonly ISawmill _sawmill = Logger.GetSawmill("wiki");
    private string? _articleSearchId;
    private string _articleSearchQuery = string.Empty;
    private int _articleSearchHit;
    private int _articleSearchGeneration;
    private bool _loaded;
    private WikiLoadStats _stats;

    public bool IsLoaded => _loaded;
    public WikiLoadStats Stats => _stats;

    /// <summary>
    /// Loaded and sanitized wiki articles. Accessing this property lazily loads the wiki database.
    /// </summary>
    public IReadOnlyCollection<WikiArticleDocument> Articles
    {
        get
        {
            EnsureLoaded();
            return _articles.Values;
        }
    }

    public void Reload()
    {
        ReloadInternal();
        _loaded = true;
    }

    private void EnsureLoaded()
    {
        if (_loaded)
            return;

        Reload();
    }

    private void ReloadInternal()
    {
        _articles.Clear();
        var totalBytes = 0;
        var skipped = 0;
        var hitArticleLimit = false;
        var hitByteLimit = false;

        foreach (var article in _prototype.EnumeratePrototypes<WikiArticlePrototype>())
        {
            if (_articles.Count >= MaxArticles)
            {
                hitArticleLimit = true;
                _sawmill.Warning($"Wiki article limit of {MaxArticles} reached, skipping remaining articles");
                break;
            }

            if (!IsAllowedArticleFile(article.File))
            {
                skipped++;
                _sawmill.Warning($"Wiki article {article.ID} uses disallowed file path {article.File}");
                continue;
            }

            if (!TryReadText(article.File, MaxArticleBytes, out var html))
            {
                skipped++;
                _sawmill.Warning($"Failed to load wiki article {article.ID} from {article.File}");
                continue;
            }

            totalBytes += Encoding.UTF8.GetByteCount(html);
            if (totalBytes > MaxTotalArticleBytes)
            {
                hitByteLimit = true;
                _sawmill.Warning($"Wiki article byte budget of {MaxTotalArticleBytes} exceeded at {article.ID}, skipping remaining articles");
                break;
            }

            try
            {
                var sanitized = WikiHtmlSanitizer.SanitizeFragment(html);
                var plainText = WikiHtmlSanitizer.SanitizedToPlainText(sanitized);
                _articles[NormalizeId(article.ID)] = new WikiArticleDocument(article, sanitized, plainText);
            }
            catch (RegexMatchTimeoutException)
            {
                skipped++;
                _sawmill.Warning($"Wiki article {article.ID} exceeded sanitizer regex timeout, skipping");
            }
        }

        _stats = new WikiLoadStats(_articles.Count, skipped, totalBytes, hitArticleLimit, hitByteLimit);
    }

    public WikiArticleDocument? GetArticle(string id)
    {
        return TryGetArticle(id, out var article) ? article : null;
    }

    public bool TryGetArticle(string id, [NotNullWhen(true)] out WikiArticleDocument? article)
    {
        EnsureLoaded();
        return _articles.TryGetValue(NormalizeId(id), out article);
    }

    public bool HasArticlePrototype(string id)
    {
        return _prototype.HasIndex<WikiArticlePrototype>(NormalizeId(id));
    }

    /// <summary>
    /// Returns wiki article prototypes without reading, sanitizing, or caching the article HTML.
    /// Use this for lightweight validation, admin tools, and menu previews.
    /// </summary>
    public IReadOnlyList<WikiArticlePrototype> GetArticlePrototypes()
    {
        return _prototype.EnumeratePrototypes<WikiArticlePrototype>()
            .OrderBy(x => x.Category.ToLower(CultureInfo.CurrentCulture))
            .ThenBy(x => x.Priority)
            .ThenBy(x => x.Title.ToLower(CultureInfo.CurrentCulture))
            .ToArray();
    }

    public WikiArticleDocument? GetDefaultArticle()
    {
        EnsureLoaded();
        return GetSortedArticles(_articles.Values).FirstOrDefault();
    }

    public bool TryGetArticleIdFromUrl(string url, [NotNullWhen(true)] out string? id)
    {
        id = null;

        if (!TryParseWikiUri(url, out var uri) || uri.Host != "article")
            return false;

        id = NormalizeId(uri.Path);
        return true;
    }

    public string GetArticleUri(string articleId)
    {
        return $"wiki://article/{NormalizeId(articleId)}";
    }

    public int SetArticleSearch(string articleId, string query, int hit)
    {
        _articleSearchId = NormalizeId(articleId);
        _articleSearchQuery = NormalizeSearchQuery(query);
        _articleSearchHit = Math.Max(0, hit);
        return ++_articleSearchGeneration;
    }

    public int CountArticleSearchMatches(string articleId, string query)
    {
        EnsureLoaded();
        if (!_articles.TryGetValue(NormalizeId(articleId), out var article))
            return 0;

        query = NormalizeSearchQuery(query);
        if (query.Length == 0)
            return 0;

        return CountMatches(article.PlainText, query, MaxArticleSearchHighlights);
    }

    public IReadOnlyList<WikiArticleDocument> Search(string query)
    {
        EnsureLoaded();
        query = query.Trim();
        if (query.Length == 0)
            return GetSortedArticles(_articles.Values).ToArray();

        var tokens = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLower(CultureInfo.CurrentCulture))
            .ToArray();

        return _articles.Values
            .Select(article => (Article: article, Score: Score(article, tokens)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Article.Prototype.Title.ToLower(CultureInfo.CurrentCulture))
            .Take(50)
            .Select(x => x.Article)
            .ToArray();
    }

    public string BuildPage(WikiArticleDocument article)
    {
        var title = WikiHtmlSanitizer.HtmlEncode(article.Prototype.Title);
        var html = article.Html;
        if (_articleSearchId == NormalizeId(article.Prototype.ID) && _articleSearchQuery.Length != 0)
            html = HighlightArticleHtml(html, _articleSearchQuery, _articleSearchHit);

        return $$"""
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src wiki:; style-src wiki:; font-src wiki:;">
<link rel="stylesheet" href="wiki://style/wiki.css">
<title>{{title}}</title>
</head>
<body>
<main class="article-root">
{{html}}
</main>
</body>
</html>
""";
    }

    public void HandleResourceRequest(IRequestHandlerContext context)
    {
        if (!TryParseWikiUri(context.Url, out var uri))
        {
            context.DoCancel();
            return;
        }

        switch (uri.Host)
        {
            case "article":
                RespondArticle(context, uri.Path);
                return;
            case "style":
                var stylePath = uri.Path;
                if (!IsAllowedStylePath(stylePath))
                {
                    context.DoCancel();
                    return;
                }

                RespondResource(context, new ResPath($"{StyleRoot}{stylePath}"), "text/css", MaxArticleBytes);
                return;
            case "image":
                var path = uri.Path;
                if (!IsAllowedImagePath(path))
                {
                    context.DoCancel();
                    return;
                }

                RespondResource(context, new ResPath($"{ImageRoot}{path}"), GuessImageMime(path), MaxImageBytes);
                return;
            default:
                context.DoCancel();
                return;
        }
    }

    public void HandleBeforeBrowse(IBeforeBrowseContext context)
    {
        if (!TryParseWikiUri(context.Url, out var uri) || uri.Host != "article")
            context.DoCancel();
    }

    private void RespondArticle(IRequestHandlerContext context, string id)
    {
        EnsureLoaded();
        if (!_articles.TryGetValue(NormalizeId(id), out var article))
        {
            context.DoRespondStream(ToStream(BuildNotFoundPage(id)), "text/html");
            return;
        }

        context.DoRespondStream(ToStream(BuildPage(article)), "text/html");
    }

    private void RespondResource(IRequestHandlerContext context, ResPath path, string contentType, int maxBytes)
    {
        if (!TryOpenLimited(path, maxBytes, out var stream))
        {
            context.DoCancel();
            return;
        }

        context.DoRespondStream(stream, contentType);
    }

    private bool TryReadText(ResPath path, int maxBytes, [NotNullWhen(true)] out string? text)
    {
        text = null;

        if (!TryOpenLimited(path, maxBytes, out var stream))
            return false;

        using (stream)
        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
        {
            text = reader.ReadToEnd();
        }

        return true;
    }

    private bool TryOpenLimited(ResPath path, int maxBytes, [NotNullWhen(true)] out Stream? stream)
    {
        stream = null;

        if (!_resources.TryContentFileRead(path, out var source))
            return false;

        using (source)
        {
            if (source.CanSeek && source.Length > maxBytes)
                return false;

            var memory = new MemoryStream(source.CanSeek ? (int)Math.Min(source.Length, maxBytes) : 0);
            var buffer = new byte[8192];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (memory.Length + read > maxBytes)
                {
                    memory.Dispose();
                    return false;
                }

                memory.Write(buffer, 0, read);
            }

            memory.Position = 0;
            stream = memory;
            return true;
        }
    }

    private static bool TryParseWikiUri(string url, out WikiUri uri)
    {
        uri = default;
        const string scheme = "wiki://";

        if (!url.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = url.Substring(scheme.Length);
        var suffix = rest.IndexOfAny(new[] { '?', '#' });
        if (suffix >= 0)
            rest = rest.Substring(0, suffix);

        var slash = rest.IndexOf('/');
        if (slash <= 0 || slash >= rest.Length - 1)
            return false;

        uri = new WikiUri(
            rest.Substring(0, slash).ToLowerInvariant(),
            rest.Substring(slash + 1));
        return true;
    }

    private static int Score(WikiArticleDocument article, string[] tokens)
    {
        var score = 0;
        var title = article.Prototype.Title.ToLower(CultureInfo.CurrentCulture);
        var category = article.Prototype.Category.ToLower(CultureInfo.CurrentCulture);
        var body = article.PlainText.ToLower(CultureInfo.CurrentCulture);
        var tags = article.Prototype.Tags.Select(x => x.ToLower(CultureInfo.CurrentCulture)).ToArray();
        var aliases = article.Prototype.Aliases.Select(x => x.ToLower(CultureInfo.CurrentCulture)).ToArray();

        foreach (var token in tokens)
        {
            if (title.Contains(token))
                score += 100;
            if (aliases.Any(x => x.Contains(token)))
                score += 80;
            if (tags.Any(x => x.Contains(token)))
                score += 60;
            if (category.Contains(token))
                score += 30;
            if (body.Contains(token))
                score += 10;
        }

        return score;
    }

    private static IOrderedEnumerable<WikiArticleDocument> GetSortedArticles(IEnumerable<WikiArticleDocument> articles)
    {
        return articles
            .OrderBy(x => x.Prototype.Category.ToLower(CultureInfo.CurrentCulture))
            .ThenBy(x => x.Prototype.Priority)
            .ThenBy(x => x.Prototype.Title.ToLower(CultureInfo.CurrentCulture));
    }

    private static string HighlightArticleHtml(string html, string query, int selectedHit)
    {
        var builder = new StringBuilder(html.Length + 512);
        var index = 0;
        var hit = 0;

        while (index < html.Length)
        {
            var tagStart = html.IndexOf('<', index);
            if (tagStart < 0)
            {
                AppendHighlightedText(builder, html, index, html.Length - index, query, selectedHit, ref hit);
                break;
            }

            AppendHighlightedText(builder, html, index, tagStart - index, query, selectedHit, ref hit);

            var tagEnd = html.IndexOf('>', tagStart);
            if (tagEnd < 0)
            {
                builder.Append(html, tagStart, html.Length - tagStart);
                break;
            }

            builder.Append(html, tagStart, tagEnd - tagStart + 1);
            index = tagEnd + 1;
        }

        return builder.ToString();
    }

    private static void AppendHighlightedText(
        StringBuilder builder,
        string text,
        int start,
        int length,
        string query,
        int selectedHit,
        ref int hit)
    {
        if (length <= 0 || hit >= MaxArticleSearchHighlights)
        {
            if (length > 0)
                builder.Append(text, start, length);
            return;
        }

        var end = start + length;
        var cursor = start;
        while (cursor < end && hit < MaxArticleSearchHighlights)
        {
            var match = text.IndexOf(query, cursor, end - cursor, StringComparison.CurrentCultureIgnoreCase);
            if (match < 0)
                break;

            builder.Append(text, cursor, match - cursor);
            var currentClass = hit == selectedHit ? " wiki-search-current" : string.Empty;
            builder.Append("<mark id=\"wiki-search-hit-");
            builder.Append(hit);
            builder.Append("\" class=\"wiki-search-hit");
            builder.Append(currentClass);
            builder.Append("\">");
            builder.Append(text, match, query.Length);
            builder.Append("</mark>");

            hit++;
            cursor = match + query.Length;
        }

        builder.Append(text, cursor, end - cursor);
    }

    private static int CountMatches(string text, string query, int maxMatches)
    {
        var count = 0;
        var index = 0;
        while (index < text.Length && count < maxMatches)
        {
            var match = text.IndexOf(query, index, StringComparison.CurrentCultureIgnoreCase);
            if (match < 0)
                break;

            count++;
            index = match + query.Length;
        }

        return count;
    }

    private static string NormalizeSearchQuery(string query)
    {
        query = query.Trim();
        if (query.Length < MinArticleSearchLength)
            return string.Empty;
        if (query.Length > MaxArticleSearchLength)
            return query.Substring(0, MaxArticleSearchLength);
        return query;
    }

    private static bool IsAllowedImagePath(string path)
    {
        if (!IsSafeRelativePath(path))
            return false;

        return path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedStylePath(string path)
    {
        return IsSafeRelativePath(path)
               && path.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedArticleFile(ResPath path)
    {
        var value = path.ToString();
        return value.StartsWith(ArticleRoot, StringComparison.Ordinal)
               && IsSafeRelativePath(value.Substring(ArticleRoot.Length))
               && (value.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                   || value.EndsWith(".htm", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Contains("..", StringComparison.Ordinal)
            || path.Contains('\\')
            || path.StartsWith("/", StringComparison.Ordinal)
            || path.Contains("//", StringComparison.Ordinal))
            return false;

        foreach (var c in path)
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '/' || c == '-' || c == '_' || c == '.')
                continue;

            return false;
        }

        return true;
    }

    private static string GuessImageMime(string path)
    {
        if (path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            return "image/jpeg";
        if (path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            return "image/webp";
        return "image/png";
    }

    private static Stream ToStream(string text)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(text));
    }

    private static string BuildNotFoundPage(string id)
    {
        var safeId = WikiHtmlSanitizer.HtmlEncode(id);
        return $$"""
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src wiki:; style-src wiki:; font-src wiki:;">
<link rel="stylesheet" href="wiki://style/wiki.css">
</head>
<body><main class="article-root"><h1>Article not found</h1><p>No wiki article exists for <code>{{safeId}}</code>.</p></main></body>
</html>
""";
    }

    private static string NormalizeId(string id)
    {
        return id.ToLowerInvariant();
    }

    private readonly record struct WikiUri(string Host, string Path);
}
