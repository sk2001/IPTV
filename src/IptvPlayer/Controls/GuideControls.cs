using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IptvPlayer.Models;

namespace IptvPlayer.Controls;

internal static class GuideStyle
{
    public const double PxPerMinute = 5.0;   // 300 px par heure

    public static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Text, Segoe UI"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static readonly Brush Cell = Freeze(new SolidColorBrush(Color.FromRgb(0x1D, 0x24, 0x30)));
    public static readonly Brush Past = Freeze(new SolidColorBrush(Color.FromRgb(0x16, 0x1B, 0x24)));
    public static readonly Brush Live = Freeze(new SolidColorBrush(Color.FromRgb(0x1B, 0x33, 0x58)));
    public static readonly Brush LiveBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF)));
    public static readonly Brush Text = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xED, 0xF3)));
    public static readonly Brush Muted = Freeze(new SolidColorBrush(Color.FromRgb(0x8B, 0x98, 0xA9)));
    public static readonly Brush Dim = Freeze(new SolidColorBrush(Color.FromRgb(0x5E, 0x6A, 0x7A)));
    public static readonly Brush Line = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x32, 0x3D)));
    public static readonly Brush NowLine = Freeze(new SolidColorBrush(Color.FromRgb(0xF8, 0x51, 0x49)));
    public static readonly Pen LiveOutline = FreezePen(new Pen(LiveBorder, 1));

    private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    private static Pen FreezePen(Pen p) { p.Freeze(); return p; }

    public static FormattedText Format(string text, double size, Brush brush, double maxWidth, double dpi) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, size, brush, dpi)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
}

/// <summary>Ligne de programmes d'une chaîne, dessinée directement (très rapide, aucune virtualisation à gérer).</summary>
public sealed class GuideStrip : FrameworkElement
{
    public static readonly DependencyProperty EntriesProperty = DependencyProperty.Register(
        nameof(Entries), typeof(IReadOnlyList<EpgEntry>), typeof(GuideStrip),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ViewStartProperty = DependencyProperty.Register(
        nameof(ViewStart), typeof(DateTime), typeof(GuideStrip),
        new FrameworkPropertyMetadata(DateTime.Now, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Horloge (mise à jour chaque minute) : force le redessin de l'émission en cours.</summary>
    public static readonly DependencyProperty ClockProperty = DependencyProperty.Register(
        nameof(Clock), typeof(DateTime), typeof(GuideStrip),
        new FrameworkPropertyMetadata(DateTime.Now, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<EpgEntry>? Entries { get => (IReadOnlyList<EpgEntry>?)GetValue(EntriesProperty); set => SetValue(EntriesProperty, value); }
    public DateTime ViewStart { get => (DateTime)GetValue(ViewStartProperty); set => SetValue(ViewStartProperty, value); }
    public DateTime Clock { get => (DateTime)GetValue(ClockProperty); set => SetValue(ClockProperty, value); }

    private EpgEntry? _hover;

    public GuideStrip()
    {
        ClipToBounds = true;
        ToolTipService.SetInitialShowDelay(this, 350);
        ToolTipService.SetShowDuration(this, 20000);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));   // zone cliquable
        var entries = Entries;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var start = ViewStart;
        var now = DateTime.Now;

        if (entries != null)
        {
            foreach (var e in entries)
            {
                if (e.End <= start) continue;
                var x1 = (e.Start - start).TotalMinutes * GuideStyle.PxPerMinute;
                if (x1 >= w) break;
                var x2 = (e.End - start).TotalMinutes * GuideStyle.PxPerMinute;

                var left = Math.Max(0, x1);
                var rect = new Rect(left + 1, 3, Math.Min(w, x2) - left - 2, h - 6);
                if (rect.Width < 3) continue;

                var isNow = e.Start <= now && e.End > now;
                var isPast = e.End <= now;
                dc.DrawRoundedRectangle(isNow ? GuideStyle.Live : isPast ? GuideStyle.Past : GuideStyle.Cell,
                    isNow ? GuideStyle.LiveOutline : null, rect, 6, 6);

                if (rect.Width < 28) continue;
                var tw = rect.Width - 16;
                var title = GuideStyle.Format(e.Title, 12.5, isPast ? GuideStyle.Muted : GuideStyle.Text, tw, dpi);
                dc.DrawText(title, new Point(rect.X + 8, rect.Y + 6));
                var time = GuideStyle.Format($"{e.Start:HH:mm} – {e.End:HH:mm}", 10.5, isPast ? GuideStyle.Dim : GuideStyle.Muted, tw, dpi);
                dc.DrawText(time, new Point(rect.X + 8, rect.Y + 25));
            }
        }

        var nx = (now - start).TotalMinutes * GuideStyle.PxPerMinute;
        if (nx >= 0 && nx <= w) dc.DrawRectangle(GuideStyle.NowLine, null, new Rect(nx - 1, 0, 2, h));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var entries = Entries;
        if (entries == null) return;
        var t = ViewStart.AddMinutes(e.GetPosition(this).X / GuideStyle.PxPerMinute);
        var hit = entries.FirstOrDefault(x => x.Start <= t && x.End > t);
        if (ReferenceEquals(hit, _hover)) return;
        _hover = hit;
        ToolTip = hit == null ? null
            : $"{hit.Title}\n{hit.Start:HH:mm} – {hit.End:HH:mm}" + (string.IsNullOrWhiteSpace(hit.Description) ? "" : "\n\n" + hit.Description);
        if (ToolTip is string) ToolTipService.SetPlacement(this, System.Windows.Controls.Primitives.PlacementMode.Mouse);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        ToolTip = null;
    }
}

/// <summary>Règle horaire (graduations demi-heure, libellés à l'heure).</summary>
public sealed class GuideRuler : FrameworkElement
{
    public static readonly DependencyProperty ViewStartProperty = DependencyProperty.Register(
        nameof(ViewStart), typeof(DateTime), typeof(GuideRuler),
        new FrameworkPropertyMetadata(DateTime.Now, FrameworkPropertyMetadataOptions.AffectsRender));

    public DateTime ViewStart { get => (DateTime)GetValue(ViewStartProperty); set => SetValue(ViewStartProperty, value); }

    public GuideRuler() => ClipToBounds = true;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var start = ViewStart;

        var tick = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute >= 30 ? 30 : 0, 0);
        if (tick < start) tick = tick.AddMinutes(30);

        for (; ; tick = tick.AddMinutes(30))
        {
            var x = (tick - start).TotalMinutes * GuideStyle.PxPerMinute;
            if (x > w) break;
            var full = tick.Minute == 0;
            dc.DrawRectangle(GuideStyle.Line, null, new Rect(x, full ? 4 : h - 8, 1, full ? h - 4 : 8));
            if (full)
            {
                var label = tick.Hour == 0 ? tick.ToString("ddd d MMM", CultureInfo.CurrentCulture) : tick.ToString("HH:mm");
                dc.DrawText(GuideStyle.Format(label, 12, GuideStyle.Muted, 140, dpi), new Point(x + 6, 6));
            }
        }
    }
}
