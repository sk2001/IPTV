using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;

namespace IptvPlayer.Markdown;

/// <summary>
/// Rendu Markdown → <see cref="FlowDocument"/> aux couleurs de l'application (sans dépendance externe).
/// Gère : titres, paragraphes, gras / italique, code en ligne et blocs, listes (imbriquées), citations,
/// tableaux, séparateurs, liens http(s) et adresses automatiques.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly FontFamily UiFont = new("Segoe UI Variable Text, Segoe UI");
    private static readonly FontFamily CodeFont = new("Cascadia Mono, Consolas");

    private static readonly Regex ItemRx = new(@"^(\s*)([*+-]|\d+[.)])\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex HeadingRx = new(@"^(#{1,6})\s+(.*?)\s*#*\s*$", RegexOptions.Compiled);
    private static readonly Regex RuleRx = new(@"^\s*([-*_])(\s*\1){2,}\s*$", RegexOptions.Compiled);
    private static readonly Regex TableSepRx = new(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$", RegexOptions.Compiled);

    private static readonly Regex InlineRx = new(
        @"`(?<code>[^`]+)`" +
        @"|\*\*(?<b>.+?)\*\*" +
        @"|__(?<b2>.+?)__" +
        @"|\[(?<lt>[^\]]+)\]\((?<lu>[^)\s]+)(?:\s+""[^""]*"")?\)" +
        @"|<(?<au>https?://[^>\s]+)>" +
        @"|(?<![*\w])\*(?<i>[^*\s](?:[^*]*[^*\s])?)\*(?!\*)" +
        @"|(?<!\w)_(?<i2>[^_\s](?:[^_]*[^_\s])?)_(?!\w)" +
        @"|(?<url>https?://[^\s<>)\]]+)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Couleurs (reprises des ressources de l'application, avec repli).
    private static Brush Res(string key, Brush fallback) => Application.Current?.TryFindResource(key) as Brush ?? fallback;
    private sealed record Palette(Brush Text, Brush Muted, Brush Accent, Brush Code, Brush Line);
    private static Palette Colors() => new(
        Res("TextBrush", Brushes.White), Res("MutedBrush", Brushes.Gray), Res("AccentHoverBrush", Brushes.CornflowerBlue),
        Res("Surface2Brush", Brushes.DimGray), Res("LineBrush", Brushes.DimGray));

    public static FlowDocument Render(string markdown)
    {
        var c = Colors();
        var doc = new FlowDocument
        {
            FontFamily = UiFont,
            FontSize = 13.5,
            Foreground = c.Text,
            PagePadding = new Thickness(22, 16, 22, 20),
            LineHeight = 21,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        doc.AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(OnNavigate));

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

            // Bloc de code « ``` »
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                i++;
                var code = new List<string>();
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal)) code.Add(lines[i++]);
                i++;   // fence fermante
                doc.Blocks.Add(CodeBlock(code, c));
                continue;
            }

            var h = HeadingRx.Match(line);
            if (h.Success) { doc.Blocks.Add(Heading(h.Groups[1].Length, h.Groups[2].Value, c)); i++; continue; }

            if (RuleRx.IsMatch(line))
            {
                doc.Blocks.Add(new Paragraph(new Run(" ")) { FontSize = 2, Margin = new Thickness(0, 8, 0, 14), BorderBrush = c.Line, BorderThickness = new Thickness(0, 0, 0, 1) });
                i++;
                continue;
            }

            if (line.TrimStart().StartsWith('>'))
            {
                var sb = new StringBuilder();
                while (i < lines.Length && lines[i].TrimStart().StartsWith('>'))
                    sb.Append(lines[i++].TrimStart().TrimStart('>').Trim()).Append(' ');
                var p = new Paragraph { Margin = new Thickness(0, 2, 0, 12), Padding = new Thickness(14, 6, 6, 6), BorderBrush = c.Accent,
                                        BorderThickness = new Thickness(3, 0, 0, 0), Foreground = c.Muted, Background = c.Code };
                AddInlines(p.Inlines, sb.ToString().Trim(), c);
                doc.Blocks.Add(p);
                continue;
            }

            if (line.TrimStart().StartsWith('|') && i + 1 < lines.Length && TableSepRx.IsMatch(lines[i + 1]))
            {
                doc.Blocks.Add(ParseTable(lines, ref i, c));
                continue;
            }

            if (ItemRx.IsMatch(line))
            {
                doc.Blocks.Add(ParseList(lines, ref i, ItemRx.Match(line).Groups[1].Length, c));
                continue;
            }

            // Paragraphe : lignes consécutives jointes par une espace.
            var text = new StringBuilder();
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && (text.Length == 0 || !StartsBlock(lines, i)))
            {
                text.Append(lines[i].Trim()).Append(' ');
                i++;
            }
            var para = new Paragraph { Margin = new Thickness(0, 0, 0, 12) };
            AddInlines(para.Inlines, text.ToString().Trim(), c);
            doc.Blocks.Add(para);
        }
        return doc;
    }

    private static bool StartsBlock(string[] lines, int i)
    {
        var l = lines[i];
        var t = l.TrimStart();
        return t.StartsWith('#') || t.StartsWith("```", StringComparison.Ordinal) || t.StartsWith('>')
               || ItemRx.IsMatch(l) || RuleRx.IsMatch(l)
               || (t.StartsWith('|') && i + 1 < lines.Length && TableSepRx.IsMatch(lines[i + 1]));
    }

    private static Paragraph Heading(int level, string text, Palette c)
    {
        var size = level switch { 1 => 26.0, 2 => 20.0, 3 => 16.5, _ => 14.5 };
        var p = new Paragraph
        {
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            LineHeight = size * 1.35,
            Margin = level switch { 1 => new Thickness(0, 4, 0, 12), 2 => new Thickness(0, 20, 0, 10), _ => new Thickness(0, 14, 0, 8) },
        };
        if (level <= 2) { p.BorderBrush = c.Line; p.BorderThickness = new Thickness(0, 0, 0, 1); p.Padding = new Thickness(0, 0, 0, 5); }
        if (level == 2) p.Foreground = c.Accent;
        AddInlines(p.Inlines, text, c);
        return p;
    }

    private static Paragraph CodeBlock(List<string> lines, Palette c)
    {
        var p = new Paragraph
        {
            FontFamily = CodeFont, FontSize = 12.5, LineHeight = 18,
            Background = c.Code, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 2, 0, 14),
        };
        for (var k = 0; k < lines.Count; k++)
        {
            if (k > 0) p.Inlines.Add(new LineBreak());
            p.Inlines.Add(new Run(lines[k]));
        }
        return p;
    }

    private static List ParseList(string[] lines, ref int i, int baseIndent, Palette c)
    {
        var first = ItemRx.Match(lines[i]);
        var ordered = char.IsDigit(first.Groups[2].Value[0]);
        var list = new List
        {
            MarkerStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(22, 0, 0, 0),
        };

        while (i < lines.Length)
        {
            var m = ItemRx.Match(lines[i]);
            if (!m.Success) break;
            var indent = m.Groups[1].Length;
            if (indent < baseIndent) break;
            if (indent > baseIndent)
            {
                if (list.ListItems.LastListItem is { } last) { last.Blocks.Add(ParseList(lines, ref i, indent, c)); continue; }
                break;
            }

            var sb = new StringBuilder(m.Groups[3].Value);
            i++;
            while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !ItemRx.IsMatch(lines[i]) && !StartsBlock(lines, i))
                sb.Append(' ').Append(lines[i++].Trim());

            var p = new Paragraph { Margin = new Thickness(0, 0, 0, 3) };
            AddInlines(p.Inlines, sb.ToString(), c);
            list.ListItems.Add(new ListItem(p));

            // Une ligne vide entre deux éléments d'une même liste.
            if (i + 1 < lines.Length && string.IsNullOrWhiteSpace(lines[i]) && ItemRx.IsMatch(lines[i + 1])
                && ItemRx.Match(lines[i + 1]).Groups[1].Length >= baseIndent) i++;
        }
        return list;
    }

    private static Table ParseTable(string[] lines, ref int i, Palette c)
    {
        var header = SplitRow(lines[i]);
        i += 2;                                    // en-tête + ligne de séparation
        var rows = new List<List<string>>();
        while (i < lines.Length && lines[i].TrimStart().StartsWith('|')) rows.Add(SplitRow(lines[i++]));

        var columns = Math.Max(header.Count, rows.Count == 0 ? 0 : rows.Max(r => r.Count));
        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 2, 0, 14),
            BorderBrush = c.Line,
            BorderThickness = new Thickness(1, 1, 0, 0),
        };
        for (var k = 0; k < columns; k++) table.Columns.Add(new TableColumn());

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        group.Rows.Add(MakeRow(header, columns, c, isHeader: true));
        foreach (var r in rows) group.Rows.Add(MakeRow(r, columns, c, isHeader: false));
        return table;
    }

    private static TableRow MakeRow(List<string> cells, int columns, Palette c, bool isHeader)
    {
        var row = new TableRow();
        if (isHeader) row.Background = c.Code;
        for (var k = 0; k < columns; k++)
        {
            var p = new Paragraph { Margin = new Thickness(0), LineHeight = 19 };
            if (isHeader) p.FontWeight = FontWeights.SemiBold;
            AddInlines(p.Inlines, k < cells.Count ? cells[k] : "", c);
            row.Cells.Add(new TableCell(p)
            {
                Padding = new Thickness(10, 6, 10, 6),
                BorderBrush = c.Line,
                BorderThickness = new Thickness(0, 0, 1, 1),
            });
        }
        return row;
    }

    private static List<string> SplitRow(string line)
    {
        var t = line.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|') && !t.EndsWith("\\|", StringComparison.Ordinal)) t = t[..^1];
        return Regex.Split(t, @"(?<!\\)\|").Select(x => x.Replace("\\|", "|").Trim()).ToList();
    }

    // ------------------------------------------------------------------ Mise en forme en ligne

    private static void AddInlines(InlineCollection target, string text, Palette c,
                                   FontWeight? weight = null, FontStyle? style = null, bool inLink = false)
    {
        var pos = 0;
        foreach (Match m in InlineRx.Matches(text))
        {
            if (m.Index > pos) AddPlain(target, text[pos..m.Index], weight, style);
            pos = m.Index + m.Length;

            // Dans le texte d'un lien, pas de lien imbriqué (évite aussi toute récursion infinie sur une URL).
            if (inLink && (m.Groups["lt"].Success || m.Groups["au"].Success || m.Groups["url"].Success))
            {
                AddPlain(target, m.Groups["lt"].Success ? m.Groups["lt"].Value : m.Groups["au"].Success ? m.Groups["au"].Value : m.Groups["url"].Value, weight, style);
                continue;
            }

            if (m.Groups["code"].Success)
            {
                target.Add(new Run(m.Groups["code"].Value) { FontFamily = CodeFont, FontSize = 12.5, Background = c.Code, Foreground = c.Accent });
            }
            else if (m.Groups["b"].Success || m.Groups["b2"].Success)
            {
                var span = new Span();
                AddInlines(span.Inlines, m.Groups["b"].Success ? m.Groups["b"].Value : m.Groups["b2"].Value, c, FontWeights.SemiBold, style, inLink);
                target.Add(span);
            }
            else if (m.Groups["i"].Success || m.Groups["i2"].Success)
            {
                var span = new Span();
                AddInlines(span.Inlines, m.Groups["i"].Success ? m.Groups["i"].Value : m.Groups["i2"].Value, c, weight, FontStyles.Italic, inLink);
                target.Add(span);
            }
            else if (m.Groups["lt"].Success)
            {
                target.Add(MakeLink(m.Groups["lt"].Value, m.Groups["lu"].Value, c, weight, style));
            }
            else if (m.Groups["au"].Success)
            {
                target.Add(MakeLink(m.Groups["au"].Value, m.Groups["au"].Value, c, weight, style));
            }
            else if (m.Groups["url"].Success)
            {
                var url = m.Groups["url"].Value;
                var trimmed = url.TrimEnd('.', ',', ';', ':', '!', '?');
                target.Add(MakeLink(trimmed, trimmed, c, weight, style));
                if (trimmed.Length < url.Length) AddPlain(target, url[trimmed.Length..], weight, style);
            }
        }
        if (pos < text.Length) AddPlain(target, text[pos..], weight, style);
    }

    private static void AddPlain(InlineCollection target, string text, FontWeight? weight, FontStyle? style)
    {
        text = Regex.Replace(text, @"\\([\\`*_\[\]()#+\-.!|<>])", "$1");   // échappements Markdown
        var run = new Run(text);
        if (weight != null) run.FontWeight = weight.Value;
        if (style != null) run.FontStyle = style.Value;
        target.Add(run);
    }

    private static Inline MakeLink(string label, string target, Palette c, FontWeight? weight, FontStyle? style)
    {
        var span = new Span();
        AddInlines(span.Inlines, label, c, weight, style, inLink: true);

        // Seuls les liens http(s) sont cliquables ; les chemins relatifs sont affichés comme du texte distinct.
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            var link = new Hyperlink(span) { NavigateUri = uri, Foreground = c.Accent, ToolTip = uri.AbsoluteUri };
            return link;
        }
        span.Foreground = c.Accent;
        return span;
    }

    private static void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        if (e.Uri is { Scheme: "http" or "https" } uri)
        {
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
        }
    }
}
