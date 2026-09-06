using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace Puppeteer.App.Controls;

/// <summary>Renders a markdown string for reading, GitHub README style.
///
/// Deliberately small: it covers what the state docs, the index and a project README actually contain
/// — headings, prose, lists, tables, fenced code, links, images and emphasis — and shows anything it
/// does not recognise as the plain text it is, rather than dropping it. Nothing here writes.</summary>
public sealed class MarkdownViewer : ContentControl
{
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(nameof(Markdown), typeof(string), typeof(MarkdownViewer),
            new PropertyMetadata(null, (d, _) => ((MarkdownViewer)d).Rebuild()));

    public string? Markdown { get => (string?)GetValue(MarkdownProperty); set => SetValue(MarkdownProperty, value); }

    /// <summary>The folder a relative image path resolves against — the directory of the file being read.</summary>
    public static readonly DependencyProperty BasePathProperty =
        DependencyProperty.Register(nameof(BasePath), typeof(string), typeof(MarkdownViewer),
            new PropertyMetadata(null, (d, _) => ((MarkdownViewer)d).Rebuild()));

    public string? BasePath { get => (string?)GetValue(BasePathProperty); set => SetValue(BasePathProperty, value); }

    private readonly FlowDocumentScrollViewer _viewer = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        IsToolBarVisible = false,
        Padding = new(0),
        Background = Brushes.Transparent,
        BorderThickness = new(0),
    };

    public MarkdownViewer()
    {
        Content = _viewer;
        AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(Navigate));
        Rebuild();
    }

    private void Navigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        // Only http(s) opens a browser. A relative markdown link points inside the vault and is not
        // ours to follow from here.
        if (e.Uri.IsAbsoluteUri && e.Uri.Scheme is "http" or "https")
            try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
    }

    private void Rebuild()
    {
        var document = new FlowDocument
        {
            FontFamily = (FontFamily?)TryFindResource("UiFont") ?? new FontFamily("Segoe UI"),
            FontSize = 13,
            Foreground = Brush("TextBrush", Brushes.White),
            Background = Brushes.Transparent,
            PagePadding = new(0, 0, 12, 24),
            LineHeight = 20,
        };
        foreach (var block in Parse(Markdown ?? "")) document.Blocks.Add(block);
        _viewer.Document = document;
    }

    private Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private IEnumerable<Block> Parse(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var paragraph = new List<string>();
        var blocks = new List<Block>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            var tight = paragraph.Count == 1 && IsFieldLine(paragraph[0]);
            blocks.Add(new Paragraph(Inline(string.Join(' ', paragraph))) { Margin = new(0, 0, 0, tight ? 2 : 12) });
            paragraph.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal); i++) code.Add(lines[i]);
                blocks.Add(CodeBlock(string.Join('\n', code)));
                continue;
            }

            if (trimmed.Length == 0) { FlushParagraph(); continue; }

            if (trimmed.StartsWith('#') && trimmed.IndexOf(' ') is > 0 and var space && trimmed[..space].All(c => c == '#'))
            {
                FlushParagraph();
                blocks.Add(Heading(trimmed[(space + 1)..].Trim(), space));
                continue;
            }

            if (IsRule(trimmed)) { FlushParagraph(); blocks.Add(Rule()); continue; }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal) || trimmed == ">")
            {
                FlushParagraph();
                var quoted = new List<string>();
                for (; i < lines.Length && (lines[i].TrimStart().StartsWith('>')); i++)
                    quoted.Add(lines[i].TrimStart().TrimStart('>').Trim());
                i--;
                blocks.Add(Quote(string.Join(' ', quoted)));
                continue;
            }

            if (trimmed.StartsWith('|') && i + 1 < lines.Length && IsTableSeparator(lines[i + 1]))
            {
                FlushParagraph();
                var rows = new List<string>();
                for (; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++) rows.Add(lines[i].Trim());
                i--;
                blocks.Add(Table(rows));
                continue;
            }

            if (ImageOnly(trimmed) is { } image) { FlushParagraph(); blocks.Add(ImageBlock(image.Alt, image.Src)); continue; }

            if (BulletText(trimmed) is not null)
            {
                FlushParagraph();
                var items = new List<(int Indent, string Text)>();
                for (; i < lines.Length; i++)
                {
                    var candidate = lines[i];
                    if (BulletText(candidate.TrimStart()) is not { } text) break;
                    items.Add((candidate.Length - candidate.TrimStart().Length, text));
                }
                i--;
                blocks.Add(BulletList(items));
                continue;
            }

            // Markdown joins consecutive lines into one paragraph, but these docs write their header
            // as one **Key:** value per line and mean each to stand alone — as does any line the
            // author ended with a hard break.
            if (IsFieldLine(trimmed)) { FlushParagraph(); paragraph.Add(trimmed); FlushParagraph(); continue; }
            paragraph.Add(trimmed);
            if (line.EndsWith("  ", StringComparison.Ordinal)) FlushParagraph();
        }
        FlushParagraph();
        return blocks;
    }

    private static bool IsFieldLine(string trimmed) =>
        trimmed.StartsWith("**", StringComparison.Ordinal) && trimmed.IndexOf(":**", 2, StringComparison.Ordinal) > 0;

    private static string? BulletText(string trimmed)
    {
        if (trimmed.Length > 2 && trimmed[0] is '-' or '*' or '+' && trimmed[1] == ' ') return trimmed[2..].Trim();
        var dot = trimmed.IndexOf('.');
        if (dot is > 0 and < 4 && trimmed[..dot].All(char.IsDigit) && dot + 1 < trimmed.Length && trimmed[dot + 1] == ' ')
            return trimmed[(dot + 2)..].Trim();
        return null;
    }

    private static (string Alt, string Src)? ImageOnly(string trimmed)
    {
        if (!trimmed.StartsWith("![", StringComparison.Ordinal) || !trimmed.EndsWith(")", StringComparison.Ordinal)) return null;
        var close = trimmed.IndexOf("](", StringComparison.Ordinal);
        if (close < 0) return null;
        return (trimmed[2..close], trimmed[(close + 2)..^1]);
    }

    private Block ImageBlock(string alt, string src)
    {
        if (LoadImage(src) is not { } bitmap)
            return new Paragraph(new Run($"[image: {(alt.Length > 0 ? alt : src)}]"))
            {
                Foreground = Brush("FaintBrush", Brushes.Gray),
                FontStyle = FontStyles.Italic,
                Margin = new(0, 0, 0, 12),
            };
        var image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            MaxWidth = 720,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        return new BlockUIContainer(image) { Margin = new(0, 4, 0, 14) };
    }

    /// <summary>Loads a README image from an http(s) URL or a path relative to <see cref="BasePath"/>.
    /// Anything that fails to resolve or decode is treated as absent, not fatal — a broken badge link
    /// should not stop the rest of the doc from rendering.</summary>
    private BitmapImage? LoadImage(string src)
    {
        try
        {
            Uri uri;
            if (Uri.TryCreate(src, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https") uri = absolute;
            else
            {
                if (string.IsNullOrEmpty(BasePath)) return null;
                var full = Path.GetFullPath(Path.Combine(BasePath, src));
                if (!File.Exists(full)) return null;
                uri = new Uri(full);
            }
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = uri;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }

    private static bool IsRule(string trimmed) =>
        trimmed.Length >= 3 && (trimmed.All(c => c == '-') || trimmed.All(c => c == '*') || trimmed.All(c => c == '_'));

    private static bool IsTableSeparator(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith('|') && trimmed.Trim('|').Split('|').All(c => c.Trim().Length > 0 && c.Trim().All(ch => ch is '-' or ':'));
    }

    private Block Heading(string text, int level) => new Paragraph(Inline(text))
    {
        FontSize = level switch { 1 => 22, 2 => 17, 3 => 14.5, _ => 13.5 },
        FontWeight = FontWeights.SemiBold,
        Foreground = level <= 2 ? Brush("TextBrush", Brushes.White) : Brush("TextDimBrush", Brushes.LightGray),
        Margin = new(0, level == 1 ? 0 : 18, 0, level == 1 ? 14 : 8),
    };

    private Block Rule() => new BlockUIContainer(new Border
    {
        Height = 1,
        Background = Brush("BorderBrush", Brushes.Gray),
        Margin = new(0, 8, 0, 16),
    });

    private Block CodeBlock(string code) => new Paragraph(new Run(code))
    {
        FontFamily = (FontFamily?)TryFindResource("MonoFont") ?? new FontFamily("Consolas"),
        FontSize = 12,
        Background = Brush("ChipBrush", Brushes.Black),
        Foreground = Brush("TextDimBrush", Brushes.LightGray),
        Padding = new(12, 10, 12, 10),
        Margin = new(0, 0, 0, 14),
        LineHeight = 17,
    };

    private Block Quote(string text)
    {
        var paragraph = new Paragraph(Inline(text))
        {
            Foreground = Brush("MutedTextBrush", Brushes.Gray),
            Padding = new(12, 2, 0, 2),
            Margin = new(0, 0, 0, 12),
            BorderBrush = Brush("AccentBrush", Brushes.Goldenrod),
            BorderThickness = new(2, 0, 0, 0),
        };
        return paragraph;
    }

    private Block BulletList(IReadOnlyList<(int Indent, string Text)> items)
    {
        var list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new(0, 0, 0, 12), Padding = new(18, 0, 0, 0) };
        List? nested = null;
        var baseIndent = items.Count == 0 ? 0 : items.Min(x => x.Indent);
        foreach (var (indent, text) in items)
        {
            var item = new ListItem(new Paragraph(Inline(text)) { Margin = new(0, 0, 0, 4) });
            if (indent > baseIndent && list.ListItems.Count > 0)
            {
                if (nested is null)
                {
                    nested = new List { MarkerStyle = TextMarkerStyle.Circle, Padding = new(16, 0, 0, 0), Margin = new(0, 4, 0, 0) };
                    list.ListItems.LastListItem!.Blocks.Add(nested);
                }
                nested.ListItems.Add(item);
                continue;
            }
            nested = null;
            list.ListItems.Add(item);
        }
        return list;
    }

    private Block Table(IReadOnlyList<string> rows)
    {
        var parsed = rows.Where(r => !IsTableSeparator(r)).Select(Cells).ToArray();
        if (parsed.Length == 0) return new Paragraph();
        var columns = parsed.Max(r => r.Length);
        var table = new Table { CellSpacing = 0, Margin = new(0, 0, 0, 14) };
        for (var c = 0; c < columns; c++) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        for (var r = 0; r < parsed.Length; r++)
        {
            var row = new TableRow();
            foreach (var text in parsed[r])
                row.Cells.Add(new TableCell(new Paragraph(Inline(text)) { FontSize = 12 })
                {
                    Padding = new(8, 6, 8, 6),
                    BorderBrush = Brush("BorderSoftBrush", Brushes.Gray),
                    BorderThickness = new(0, 0, 0, 1),
                    FontWeight = r == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = r == 0 ? Brush("TextBrush", Brushes.White) : Brush("TextDimBrush", Brushes.LightGray),
                });
            for (var pad = parsed[r].Length; pad < columns; pad++) row.Cells.Add(new TableCell());
            group.Rows.Add(row);
        }
        return table;
    }

    private static string[] Cells(string row) => row.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();

    /// <summary>Inline emphasis, code and links. Unbalanced markers are left as written.</summary>
    private Span Inline(string text)
    {
        var span = new Span();
        var buffer = new System.Text.StringBuilder();
        void Flush() { if (buffer.Length > 0) { span.Inlines.Add(new Run(buffer.ToString())); buffer.Clear(); } }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '`' && Closing(text, i + 1, "`") is { } codeEnd)
            {
                Flush();
                span.Inlines.Add(new Run(text[(i + 1)..codeEnd])
                {
                    FontFamily = (FontFamily?)TryFindResource("MonoFont") ?? new FontFamily("Consolas"),
                    Foreground = Brush("AccentBrush", Brushes.Goldenrod),
                    FontSize = 12,
                });
                i = codeEnd;
                continue;
            }
            if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*' && Closing(text, i + 2, "**") is { } boldEnd)
            {
                Flush();
                span.Inlines.Add(new Bold(new Run(text[(i + 2)..boldEnd])));
                i = boldEnd + 1;
                continue;
            }
            if ((text[i] == '*' || text[i] == '_') && Closing(text, i + 1, text[i].ToString()) is { } italicEnd && italicEnd > i + 1)
            {
                Flush();
                span.Inlines.Add(new Italic(new Run(text[(i + 1)..italicEnd])));
                i = italicEnd;
                continue;
            }
            if (text[i] == '!' && i + 1 < text.Length && text[i + 1] == '[' && ImageAt(text, i + 1) is { } inlineImage)
            {
                Flush();
                span.Inlines.Add(inlineImage.Inline);
                i = inlineImage.End;
                continue;
            }
            if (text[i] == '[' && LinkAt(text, i) is { } link)
            {
                Flush();
                span.Inlines.Add(link.Inline);
                i = link.End;
                continue;
            }
            buffer.Append(text[i]);
        }
        Flush();
        return span;
    }

    private static int? Closing(string text, int from, string marker)
    {
        var at = text.IndexOf(marker, from, StringComparison.Ordinal);
        return at < 0 ? null : at;
    }

    /// <summary>A badge or inline icon written mid-sentence — sized small and baseline-aligned so a
    /// row of shields.io badges reads as a row of text, not a column of images.</summary>
    private (Inline Inline, int End)? ImageAt(string text, int start)
    {
        var close = text.IndexOf("](", start, StringComparison.Ordinal);
        if (close < 0) return null;
        var end = text.IndexOf(')', close);
        if (end < 0) return null;
        var alt = text[(start + 1)..close];
        var src = text[(close + 2)..end];
        if (LoadImage(src) is not { } bitmap) return (new Run(alt.Length > 0 ? alt : src) { FontStyle = FontStyles.Italic }, end);
        var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxHeight = 20, VerticalAlignment = VerticalAlignment.Center };
        return (new InlineUIContainer(image) { BaselineAlignment = BaselineAlignment.Center }, end);
    }

    private (Inline Inline, int End)? LinkAt(string text, int start)
    {
        var close = text.IndexOf("](", start, StringComparison.Ordinal);
        if (close < 0) return null;
        var end = text.IndexOf(')', close);
        if (end < 0) return null;
        var label = text[(start + 1)..close];
        var target = text[(close + 2)..end];
        var link = new Hyperlink(Inline(label)) { Foreground = Brush("AccentBrush", Brushes.Goldenrod), ToolTip = target };
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri)) link.NavigateUri = uri;
        return (link, end);
    }
}
