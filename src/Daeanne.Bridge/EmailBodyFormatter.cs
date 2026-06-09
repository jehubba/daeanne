using System.Net;
using System.Text.RegularExpressions;
using Markdig;

namespace Daeanne.Bridge;

internal static class EmailBodyFormatter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    internal sealed record FormattedBody(string PlainText, string Html);

    public static FormattedBody FormatMarkdown(string? markdown)
    {
        var source = markdown ?? string.Empty;
        var bodyHtml = Markdown.ToHtml(source, Pipeline);

        return new FormattedBody(
            PlainText: ToPlainText(bodyHtml),
            Html: WrapHtmlDocument(bodyHtml));
    }

    private static string WrapHtmlDocument(string bodyHtml) =>
        $@"<!DOCTYPE html>
<html>
<head>
  <meta charset=""utf-8"" />
  <style>
    body {{ font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Arial, sans-serif; line-height: 1.45; color: #1f2937; }}
    table {{ border-collapse: collapse; width: 100%; }}
    th, td {{ border: 1px solid #d1d5db; padding: 6px 8px; text-align: left; vertical-align: top; }}
    pre, code {{ font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, ""Liberation Mono"", monospace; }}
    pre {{ background: #f3f4f6; border: 1px solid #e5e7eb; border-radius: 4px; padding: 10px; overflow-x: auto; }}
    code {{ background: #f3f4f6; padding: 0.1rem 0.25rem; border-radius: 3px; }}
  </style>
</head>
<body>
{bodyHtml}
</body>
</html>";

    private static string ToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var withNewlines = Regex.Replace(
            html,
            @"<\s*(br\s*/?>|/(p|div|ul|ol|li|tr|h[1-6])\s*>)",
            "\n",
            RegexOptions.IgnoreCase);

        var stripped = Regex.Replace(withNewlines, "<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(stripped);
        var normalizedLines = decoded
            .Replace("\r", string.Empty)
            .Split('\n')
            .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
            .Where(line => line.Length > 0);

        return string.Join('\n', normalizedLines);
    }
}
