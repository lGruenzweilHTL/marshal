using System.Net;
using System.Text;
using MimeKit.Text;

namespace Marshal.Integrations.Mail.Imap;

/// <summary>Plain text from HTML mail: text content only, block elements as line breaks, no scripts or styles.</summary>
internal static class HtmlText
{
    private static readonly HashSet<string> _blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "br", "li", "tr", "h1", "h2", "h3", "h4", "h5", "h6", "table", "blockquote", "hr", "ul", "ol",
    };

    private static readonly HashSet<string> _skipped = new(StringComparer.OrdinalIgnoreCase) { "script", "style", "head", "title" };

    public static string Convert(string html)
    {
        var text = new StringBuilder();
        var tokenizer = new HtmlTokenizer(new StringReader(html));
        int skipping = 0;
        while (tokenizer.ReadNextToken(out HtmlToken? token))
        {
            switch (token)
            {
                case HtmlTagToken tag when _skipped.Contains(tag.Name):
                    skipping += tag.IsEndTag ? -1 : tag.IsEmptyElement ? 0 : 1;
                    skipping = Math.Max(0, skipping);
                    break;
                case HtmlTagToken tag when _blocks.Contains(tag.Name):
                    text.Append('\n');
                    break;
                case HtmlDataToken data when skipping == 0:
                    text.Append(WebUtility.HtmlDecode(data.Data));
                    break;
            }
        }

        string[] lines = text.ToString().Split('\n').Select(line => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))).ToArray();
        return string.Join('\n', lines.Where((line, index) => line.Length > 0 || (index > 0 && lines[index - 1].Length > 0))).Trim();
    }
}
