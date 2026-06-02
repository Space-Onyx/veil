using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace Content.Client._Onyx.Wiki;

public static class WikiHtmlSanitizer
{
    public const int MaxImagesPerArticle = 24;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex StripDangerousBlocksRegex = new(
        @"<\s*(script|iframe|object|embed|video|audio|form|input|button|link|meta|base|style)\b[^>]*>.*?<\s*/\s*\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline,
        RegexTimeout);

    private static readonly Regex StripCommentsRegex = new(
        @"<!--.*?-->",
        RegexOptions.Singleline,
        RegexTimeout);

    private static readonly Regex TagRegex = new(
        @"<\s*(?<slash>/)?\s*(?<tag>[a-zA-Z][a-zA-Z0-9]*)\b(?<attrs>[^>]*)>",
        RegexOptions.None,
        RegexTimeout);

    private static readonly Regex AttrRegex = new(
        @"(?<name>[a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*""(?<value>[^""]*)""",
        RegexOptions.None,
        RegexTimeout);

    private static readonly Regex WhitespaceRegex = new(
        @"\s+",
        RegexOptions.None,
        RegexTimeout);

    private static readonly HashSet<string> VoidTags = new()
    {
        "br", "hr", "img"
    };

    public static string SanitizeFragment(string html)
    {
        html = StripDangerousBlocksRegex.Replace(html, string.Empty);
        html = StripCommentsRegex.Replace(html, string.Empty);

        var images = 0;

        return TagRegex.Replace(html, match =>
        {
            var slash = match.Groups["slash"].Value;
            var tag = match.Groups["tag"].Value.ToLowerInvariant();
            var attrs = match.Groups["attrs"].Value;

            if (slash.Length > 0)
                return VoidTags.Contains(tag) ? string.Empty : $"</{tag}>";

            if (tag == "img")
            {
                images++;
                if (images > MaxImagesPerArticle)
                    return string.Empty;
            }

            var sanitizedAttrs = SanitizeAttributes(tag, attrs);
            var selfClosing = VoidTags.Contains(tag) ? " /" : string.Empty;
            return sanitizedAttrs.Length == 0
                ? $"<{tag}{selfClosing}>"
                : $"<{tag} {sanitizedAttrs}{selfClosing}>";
        });
    }

    public static string ToPlainText(string html)
    {
        var clean = SanitizeFragment(html);
        return SanitizedToPlainText(clean);
    }

    public static string SanitizedToPlainText(string html)
    {
        var clean = TagRegex.Replace(html, " ");
        clean = WhitespaceRegex.Replace(clean, " ");
        return HtmlDecode(clean).Trim();
    }

    private static string SanitizeAttributes(string tag, string attrs)
    {
        var builder = new StringBuilder();

        foreach (Match match in AttrRegex.Matches(attrs))
        {
            var name = match.Groups["name"].Value.ToLowerInvariant();
            var value = HtmlDecode(match.Groups["value"].Value);

            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                continue;

            if ((name == "href" || name == "src") &&
                value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                continue;


            if (tag == "a" && name == "href")
            {
                if (!IsSafeWikiUri(value, "article"))
                    continue;
            }

            if (tag == "img" && name == "src")
            {
                if (!IsSafeWikiUri(value, "image"))
                    continue;
            }

            AppendAttr(builder, name, value);

            if (tag == "img" && name == "src")
            {
                AppendAttr(builder, "loading", "lazy");
                AppendAttr(builder, "decoding", "async");
            }
        }

        return builder.ToString();
    }

    private static bool IsSafeWikiUri(string value, string host)
    {
        const string scheme = "wiki://";

        if (!value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = value.Substring(scheme.Length);
        var slash = rest.IndexOf('/');
        if (slash <= 0 || slash >= rest.Length - 1)
            return false;

        var path = rest.Substring(slash + 1);
        return rest.Substring(0, slash).Equals(host, StringComparison.OrdinalIgnoreCase)
               && IsSafeWikiPath(path);
    }

    private static bool IsSafeWikiPath(string path)
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

    private static void AppendAttr(StringBuilder builder, string name, string value)
    {
        if (builder.Length > 0)
            builder.Append(' ');

        builder.Append(name);
        builder.Append("=\"");
        builder.Append(HtmlEncode(value));
        builder.Append('"');
    }

    public static string HtmlEncode(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            switch (c)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                case '"':
                    builder.Append("&quot;");
                    break;
                case '\'':
                    builder.Append("&#39;");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    private static string HtmlDecode(string value)
    {
        return value
            .Replace("&quot;", "\"")
            .Replace("&#34;", "\"")
            .Replace("&#39;", "'")
            .Replace("&apos;", "'")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&amp;", "&");
    }
}
