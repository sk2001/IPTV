using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using IptvPlayer.Localization;
using IptvPlayer.Models;

namespace IptvPlayer;

public partial class EpisodesWindow : Window
{
    private readonly Action<Episode, bool> _toggleWatched;

    public Episode? Selected { get; private set; }

    public EpisodesWindow(MediaItem series, List<Episode> episodes, Episode? suggested, Action<Episode, bool> toggleWatched)
    {
        InitializeComponent();
        _toggleWatched = toggleWatched;
        Heading.Text = series.Name;

        var view = CollectionViewSource.GetDefaultView(episodes);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Episode.Season)));
        List.ItemsSource = view;

        var start = suggested ?? episodes.FirstOrDefault();
        List.SelectedItem = start;
        if (start != null) Loaded += (_, _) => List.ScrollIntoView(start);
        List.SelectionChanged += (_, _) => UpdatePlayLabel();
        UpdatePlayLabel();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowEffects.DarkTitleBar(this);
    }

    private void UpdatePlayLabel() =>
        PlayButton.Content = Loc.T(List.SelectedItem is Episode { ResumeMs: > 0 } ? "ep.resume" : "ep.play");

    private void Play_Click(object sender, RoutedEventArgs e) => Accept();
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not Episode ep) return;
        _toggleWatched(ep, !ep.IsWatched);
        UpdatePlayLabel();
    }

    private void List_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: Episode }) Accept();
    }

    private void Accept()
    {
        if (List.SelectedItem is not Episode ep) return;
        Selected = ep;
        DialogResult = true;
    }
}
