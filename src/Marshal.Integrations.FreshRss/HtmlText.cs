using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Marshal.Integrations.FreshRss;

/// <summary>Plain text of feed HTML: text content only, no scripts or styles, whitespace collapsed.</summary>
internal static class HtmlText
{
    private static readonly HtmlParser _parser = new();

    public static string Convert(string html)
    {
        using IDocument document = _parser.ParseDocument(html);
        foreach (IElement element in document.QuerySelectorAll("script, style").ToList())
            element.Remove();

        string text = document.Body?.TextContent ?? "";
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
