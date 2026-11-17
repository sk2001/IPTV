using System.Collections.ObjectModel;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IptvPlayer.Localization;
using IptvPlayer.Models;
using IptvPlayer.Services;
using Microsoft.Win32;

namespace IptvPlayer;

public partial class AccountsWindow : Window
{
    private readonly ObservableCollection<Account> _accounts;
    private readonly AccountStore _store = new();
    private Account? _editing;
    private bool _loading;

    public HashSet<Guid> Modified { get; } = new();

    public AccountsWindow(ObservableCollection<Account> accounts)
    {
        InitializeComponent();
        _accounts = accounts;
        AccountList.ItemsSource = _accounts;
        if (_accounts.Count > 0) AccountList.SelectedIndex = 0; else NewForm();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowEffects.DarkTitleBar(this);
    }

    private bool IsXtream => XtreamRadio.IsChecked == true;

    private void NewForm()
    {
        _loading = true;
        _editing = null;
        AccountList.SelectedItem = null;
        FormTitle.Text = Loc.T("acc.formNew");
        NameBox.Text = UrlBox.Text = UserBox.Text = PassBox.Password = "";
        XtreamRadio.IsChecked = true;
        DeleteBtn.Visibility = Visibility.Collapsed;
        Say("");
        _loading = false;
        ApplyType();
        NameBox.Focus();
    }

    private void LoadForm(Account a)
    {
        _loading = true;
        _editing = a;
        FormTitle.Text = a.Name;
        NameBox.Text = a.Name;
        UrlBox.Text = a.Url;
        UserBox.Text = a.Username;
        PassBox.Password = a.Password;
        XtreamRadio.IsChecked = a.Type == AccountType.Xtream;
        M3uRadio.IsChecked = a.Type == AccountType.M3u;
        DeleteBtn.Visibility = Visibility.Visible;
        Say("");
        _loading = false;
        ApplyType();
    }

    private void ApplyType()
    {
        var x = IsXtream;
        UrlLabel.Text = Loc.T(x ? "acc.urlXtream" : "acc.urlM3u");
        CredsPanel.Visibility = x ? Visibility.Visible : Visibility.Collapsed;
        BrowseBtn.Visibility = x ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Type_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading && IsInitialized) ApplyType();
    }

    private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (AccountList.SelectedItem is Account a) LoadForm(a);
    }

    private void New_Click(object sender, RoutedEventArgs e) => NewForm();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Playlists M3U|*.m3u;*.m3u8|Tous les fichiers|*.*" };
        if (dlg.ShowDialog(this) == true) UrlBox.Text = dlg.FileName;
    }

    /// <summary>Construit un compte depuis le formulaire (extrait les identifiants d'une URL get.php collée).</summary>
    private Account? BuildFromForm(out string? error)
    {
        error = null;
        var a = new Account
        {
            Name = NameBox.Text.Trim(),
            Type = IsXtream ? AccountType.Xtream : AccountType.M3u,
            Url = UrlBox.Text.Trim(),
            Username = UserBox.Text.Trim(),
            Password = PassBox.Password,
        };

        if (a.Url.Length == 0) { error = Loc.T("acc.err.url"); return null; }

        if (a.Type == AccountType.Xtream)
        {
            if (!a.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) a.Url = "http://" + a.Url;
            if (Uri.TryCreate(a.Url, UriKind.Absolute, out var uri))
            {
                var q = HttpUtility.ParseQueryString(uri.Query);
                if (string.IsNullOrEmpty(a.Username) && q["username"] is { Length: > 0 } u) a.Username = u;
                if (string.IsNullOrEmpty(a.Password) && q["password"] is { Length: > 0 } p) a.Password = p;
                a.Url = uri.GetLeftPart(UriPartial.Authority);
            }
            if (a.Username.Length == 0 || a.Password.Length == 0) { error = Loc.T("acc.err.creds"); return null; }
        }
        if (a.Name.Length == 0) a.Name = Uri.TryCreate(a.Url, UriKind.Absolute, out var h) && !h.IsFile ? h.Host : Loc.T("acc.defaultName");
        return a;
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        var a = BuildFromForm(out var err);
        if (a == null) { Say(err!, error: true); return; }

        TestBtn.IsEnabled = false;
        Say(Loc.T("acc.testing"));
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            if (a.Type == AccountType.Xtream)
            {
                var res = await new XtreamClient(a).TestAsync(cts.Token);
                if (res == null) Say(Loc.T("acc.ok"));
                else Say(res, error: true);
            }
            else
            {
                var cat = await M3uParser.LoadAsync(a.Url, cts.Token);
                if (cat.Items.Count > 0) Say(Loc.T("acc.playlistOk", cat.Items.Count));
                else Say(Loc.T("acc.playlistBad"), error: true);
            }
        }
        catch (Exception ex) { Say(ex.Message, error: true); }
        finally { TestBtn.IsEnabled = true; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var a = BuildFromForm(out var err);
        if (a == null) { Say(err!, error: true); return; }

        if (_editing == null)
        {
            _accounts.Add(a);
            _editing = a;
            Modified.Add(a.Id);
        }
        else
        {
            _editing.Name = a.Name; _editing.Type = a.Type; _editing.Url = a.Url;
            _editing.Username = a.Username; _editing.Password = a.Password;
            Modified.Add(_editing.Id);
        }

        _store.Save(_accounts);
        _loading = true;
        AccountList.Items.Refresh();
        AccountList.SelectedItem = _editing;
        _loading = false;
        LoadForm(_editing);
        Say(Loc.T("acc.saved"));
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_editing == null) return;
        var r = MessageBox.Show(this, Loc.T("acc.confirmDelete", _editing.Name), Loc.T("acc.confirm"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        CatalogService.DeleteCache(_editing);
        _accounts.Remove(_editing);
        _store.Save(_accounts);
        if (_accounts.Count > 0) AccountList.SelectedIndex = 0; else NewForm();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Say(string text, bool error = false)
    {
        Message.Text = text;
        Message.Foreground = error ? (Brush)FindResource("DangerBrush") : (Brush)FindResource("TextBrush");
    }
}
