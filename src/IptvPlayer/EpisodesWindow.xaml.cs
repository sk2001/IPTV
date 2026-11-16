using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using IptvPlayer.Models;

namespace IptvPlayer;

public partial class EpisodesWindow : Window
{
    public Episode? Selected { get; private set; }

    public EpisodesWindow(MediaItem series, List<Episode> episodes)
    {
        InitializeComponent();
        Heading.Text = series.Name;
        var view = CollectionViewSource.GetDefaultView(episodes);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Episode.Season)));
        List.ItemsSource = view;
        List.SelectedItem = episodes.FirstOrDefault();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowEffects.DarkTitleBar(this);
    }

    private void Play_Click(object sender, RoutedEventArgs e) => Accept();
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

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
