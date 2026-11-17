using System.Windows;
using System.Windows.Controls;
using IptvPlayer.Services;
using IptvPlayer.ViewModels;

namespace IptvPlayer;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _vm;
    private AppState S => _vm.State;

    /// <summary>L'utilisateur a demandé « Gérer les comptes » : la fenêtre principale ouvre ensuite cette boîte.</summary>
    public bool OpenAccountsRequested { get; private set; }

    public SettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        BuildChips(AudioVodChips, "audioVod", LangOptions("Auto"), S.AudioLangVod, c => S.AudioLangVod = c);
        BuildChips(SubVodChips, "subVod", LangOptions("Aucun"), S.SubLangVod, c => S.SubLangVod = c);
        BuildChips(AudioLiveChips, "audioLive", LangOptions("Auto"), S.AudioLangLive, c => S.AudioLangLive = c);
        BuildChips(ResumeChips, "resume",
            new[] { ("Oui", "1"), ("Non", "0") }, S.ResumePlayback ? "1" : "0", c => S.ResumePlayback = c == "1");
        UpdateHistoryInfo();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowEffects.DarkTitleBar(this);
    }

    private static IEnumerable<(string label, string code)> LangOptions(string none) =>
        new[] { (none, "") }.Concat(Services.Languages.All.Select(l => (l.Label, l.Code)));

    private void BuildChips(Panel panel, string group, IEnumerable<(string label, string code)> options, string current, Action<string> set)
    {
        foreach (var (label, code) in options)
        {
            var chip = new RadioButton
            {
                Content = label,
                GroupName = group,
                Tag = code,
                Style = (Style)FindResource("ChipRadio"),
                IsChecked = code == (current ?? ""),
            };
            chip.Checked += (_, _) =>
            {
                set(code);
                StateStore.Save(S);
            };
            panel.Children.Add(chip);
        }
    }

    private void UpdateHistoryInfo()
    {
        var acc = _vm.SelectedAccount?.Id ?? Guid.Empty;
        var movies = _vm.History.Recent(acc, Models.ContentKind.Movie).Count;
        var series = _vm.History.Recent(acc, Models.ContentKind.Series).Count;
        HistoryInfo.Text = $"{movies} film(s) et {series} série(s) dans l'historique de ce compte. " +
                           "Disponible dans Films / Séries → « Historique » (Suppr pour retirer un élément).";
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(this, "Effacer tout l'historique de visionnage (tous comptes) ?\nLes positions de reprise seront perdues.",
            "Effacer l'historique", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        _vm.ClearHistory();
        UpdateHistoryInfo();
    }

    private void Accounts_Click(object sender, RoutedEventArgs e)
    {
        OpenAccountsRequested = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
