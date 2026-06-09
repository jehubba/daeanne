using System.Text;
using System.Text.Json;
using Markdig;

namespace Daeanne.Tray;

/// <summary>
/// Tabbed window showing task work directory files:
/// Plan (daeanne-plan.md), Session (session.md tail), Context (context.json),
/// and any additional output files found in the directory.
/// </summary>
internal class PlanViewWindow : Form
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private const string DarkCss = """
        body {
            background: #1c1c1e; color: #ddd; font-family: 'Segoe UI', sans-serif;
            font-size: 13px; line-height: 1.6; padding: 16px 20px; margin: 0;
        }
        h1, h2, h3 { color: #fff; margin-top: 1.2em; margin-bottom: .4em; }
        h1 { font-size: 1.4em; border-bottom: 1px solid #444; padding-bottom: .3em; }
        h2 { font-size: 1.15em; }
        h3 { font-size: 1em; }
        code { background: #2a2a2e; color: #e8c07a; padding: 1px 5px; border-radius: 3px; font-family: 'Cascadia Code','Consolas',monospace; font-size: .92em; }
        pre { background: #2a2a2e; color: #c8d0da; padding: 12px; border-radius: 5px; overflow-x: auto; font-family: 'Cascadia Code','Consolas',monospace; font-size: .88em; line-height: 1.5; }
        pre code { background: none; padding: 0; color: inherit; }
        table { border-collapse: collapse; width: 100%; margin: .8em 0; }
        th { background: #2c2c30; color: #bbb; font-weight: 600; padding: 6px 10px; border: 1px solid #444; text-align: left; }
        td { padding: 5px 10px; border: 1px solid #3a3a3e; }
        tr:nth-child(even) td { background: #222226; }
        a { color: #58a6ff; }
        blockquote { border-left: 3px solid #555; margin: .5em 0; padding: .3em 1em; color: #aaa; }
        hr { border: none; border-top: 1px solid #444; margin: 1.2em 0; }
        li { margin: .2em 0; }
        .meta { color: #888; font-size: .85em; margin-bottom: 1em; }
        """;

    public PlanViewWindow(string taskId, string workDir)
    {
        Text            = $"Task — {taskId[..8]}…";
        Size            = new Size(820, 640);
        MinimumSize     = new Size(600, 400);
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = Color.FromArgb(28, 28, 30);
        ForeColor       = Color.FromArgb(220, 220, 220);
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar   = false;

        var tabs = new TabControl
        {
            Dock      = DockStyle.Fill,
            DrawMode  = TabDrawMode.OwnerDrawFixed,
            SizeMode  = TabSizeMode.Fixed,
            ItemSize  = new Size(130, 26),
            Appearance = TabAppearance.FlatButtons,
            BackColor = Color.FromArgb(28, 28, 30),
            Padding   = new Point(10, 4),
        };

        // Owner-draw tabs for dark theme
        tabs.DrawItem += (_, e) =>
        {
            bool selected = e.Index == tabs.SelectedIndex;
            var bg = selected ? Color.FromArgb(50, 50, 54) : Color.FromArgb(36, 36, 40);
            using var bgBrush = new SolidBrush(bg);
            e.Graphics.FillRectangle(bgBrush, e.Bounds);
            var fg = selected ? Color.White : Color.FromArgb(160, 160, 165);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font,
                e.Bounds, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };

        // Paint the tab strip background dark
        tabs.Paint += (_, e) =>
        {
            using var bg = new SolidBrush(Color.FromArgb(36, 36, 40));
            e.Graphics.FillRectangle(bg, e.ClipRectangle);
        };

        Controls.Add(tabs);

        // ── Discover files ─────────────────────────────────────────────────────
        var knownFiles = new (string Label, string FileName, bool IsMd, bool TailOnly)[]
        {
            ("Plan",    "daeanne-plan.md", true,  false),
            ("Session", "session.md",      true,  true),
            ("Context", "context.json",    false, false),
        };

        foreach (var (label, fileName, isMd, tailOnly) in knownFiles)
        {
            var path = Path.Combine(workDir, fileName);
            if (!File.Exists(path)) continue;
            tabs.TabPages.Add(MakeTab(label, path, isMd, tailOnly));
        }

        // Any extra output/result files
        if (Directory.Exists(workDir))
        {
            foreach (var file in Directory.GetFiles(workDir, "*", SearchOption.TopDirectoryOnly)
                .Where(f =>
                {
                    var name = Path.GetFileName(f);
                    return name != "daeanne-plan.md" && name != "session.md" && name != "context.json"
                        && (name.EndsWith(".md") || name.EndsWith(".txt") || name.EndsWith(".json"));
                })
                .OrderBy(f => f))
            {
                var name  = Path.GetFileNameWithoutExtension(file);
                var label = name.Length > 14 ? name[..14] + "…" : name;
                tabs.TabPages.Add(MakeTab(label, file, file.EndsWith(".md"), false));
            }
        }

        if (tabs.TabPages.Count == 0)
        {
            var empty = new TabPage("(empty)") { BackColor = Color.FromArgb(28, 28, 30) };
            empty.Controls.Add(new Label
            {
                Text      = $"No files found in:\n{workDir}",
                Dock      = DockStyle.Fill,
                ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleCenter
            });
            tabs.TabPages.Add(empty);
        }
    }

    private TabPage MakeTab(string label, string filePath, bool renderMarkdown, bool tailOnly)
    {
        var page = new TabPage(label)
        {
            BackColor = Color.FromArgb(28, 28, 30),
            Padding   = new Padding(0)
        };

        string content;
        try
        {
            var lines = File.ReadAllLines(filePath);
            content = tailOnly && lines.Length > 200
                ? string.Join("\n", lines.TakeLast(200))
                : string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            content = $"Could not read file: {ex.Message}";
            renderMarkdown = false;
        }

        if (renderMarkdown)
        {
            var html    = BuildHtml(content, label, filePath);
            var browser = new WebBrowser
            {
                Dock                           = DockStyle.Fill,
                ScrollBarsEnabled              = true,
                IsWebBrowserContextMenuEnabled = false,
                WebBrowserShortcutsEnabled     = false,
                AllowNavigation                = true,
            };
            page.Controls.Add(browser);

            // Navigate to blank first, then write HTML on DocumentCompleted
            browser.DocumentCompleted += (_, _) =>
            {
                if (browser.Document is null) return;
                browser.Document.OpenNew(false);
                browser.Document.Write(html);
            };
            browser.Navigate("about:blank");
        }
        else
        {
            // JSON or plain text — pretty-print JSON, show in TextBox
            if (filePath.EndsWith(".json"))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<JsonElement>(content);
                    content = JsonSerializer.Serialize(parsed,
                        new JsonSerializerOptions { WriteIndented = true });
                }
                catch { /* leave as-is */ }
            }

            var box = new TextBox
            {
                Dock        = DockStyle.Fill,
                Multiline   = true,
                ReadOnly    = true,
                ScrollBars  = ScrollBars.Both,
                WordWrap    = false,
                BackColor   = Color.FromArgb(22, 22, 24),
                ForeColor   = Color.FromArgb(200, 200, 205),
                Font        = new Font("Cascadia Code", 9f),
                BorderStyle = BorderStyle.None,
                Text        = content
            };
            page.Controls.Add(box);
        }

        return page;
    }

    private static string BuildHtml(string markdown, string title, string filePath)
    {
        var body    = Markdown.ToHtml(markdown, Pipeline);
        var modTime = File.Exists(filePath)
            ? File.GetLastWriteTime(filePath).ToString("yyyy-MM-dd HH:mm:ss")
            : "";
        return $$"""
            <!DOCTYPE html>
            <html><head>
            <meta charset="utf-8">
            <meta http-equiv="X-UA-Compatible" content="IE=edge">
            <style>{{DarkCss}}</style>
            </head><body>
            <div class="meta">{{Path.GetFileName(filePath)}} &nbsp;·&nbsp; {{modTime}}</div>
            {{body}}
            </body></html>
            """;
    }
}
