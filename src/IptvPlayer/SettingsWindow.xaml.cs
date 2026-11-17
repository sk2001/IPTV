using System.Windows;
using System.Windows.Controls;
using IptvPlayer.Localization;
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

        Loc.LanguageChanged += BuildAll;                 // les libellés « Auto / Aucun », le texte « À propos »… sont reconstruits
        Closed += (_, _) => Loc.LanguageChanged -= BuildAll;
        BuildAll();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowEffects.DarkTitleBar(this);
    }

    private void BuildAll()
    {
        foreach (var p in new Panel[] { LanguageChips, AudioVodChips, SubVodChips, AudioLiveChips, ResumeChips })
            p.Children.Clear();

        BuildChips(LanguageChips, "lang", Loc.Available.Select(l => (l.Label, l.Code)), S.Language, c =>
        {
            S.Language = c;
            StateStore.Save(S);
            Loc.SetLanguage(c);                          // application immédiate (déclenche BuildAll)
        });
        BuildChips(AudioVodChips, "audioVod", LangOptions("set.auto"), S.AudioLangVod, c => S.AudioLangVod = c);
        BuildChips(SubVodChips, "subVod", LangOptions("set.none"), S.SubLangVod, c => S.SubLangVod = c);
        BuildChips(AudioLiveChips, "audioLive", LangOptions("set.auto"), S.AudioLangLive, c => S.AudioLangLive = c);
        BuildChips(ResumeChips, "resume",
            new[] { (Loc.T("set.yes"), "1"), (Loc.T("set.no"), "0") }, S.ResumePlayback ? "1" : "0", c => S.ResumePlayback = c == "1");

        UpdateHistoryInfo();
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        AboutText.Text = Loc.T("set.about.text", $"{v?.Major}.{v?.Minor}.{v?.Build}");
    }

    private void Licenses_Click(object sender, RoutedEventArgs e) => new LicensesWindow { Owner = this }.ShowDialog();

    private static IEnumerable<(string label, string code)> LangOptions(string noneKey) =>
        new[] { (Loc.T(noneKey), "") }.Concat(Services.Languages.All.Select(l => (l.Label, l.Code)));

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
        HistoryInfo.Text = Loc.T("set.history.info", movies, series);
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(this, Loc.T("set.history.confirm"), Loc.T("set.history.confirm.title"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        _vm.ClearHistory();
        UpdateHistoryInfo();
    }

    private void Accounts_Click(object sender, RoutedEventArgs e)
    {
        OpenAccountsRequested = true;
        Close();
    }

    private void Share_Click(object sender, RoutedEventArgs e) => new ShareWindow(_vm) { Owner = this }.ShowDialog();

    private void Remote_Click(object sender, RoutedEventArgs e) => new RemoteWindow { Owner = this }.ShowDialog();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
