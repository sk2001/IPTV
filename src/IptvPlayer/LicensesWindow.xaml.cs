using System.IO;
using System.Windows;
using System.Windows.Controls;
using IptvPlayer.Localization;
using IptvPlayer.Markdown;

namespace IptvPlayer;

/// <summary>
/// Affiche les avis de licence embarqués. Un fichier au format Markdown (.md) est rendu mis en forme ;
/// les autres fichiers (LICENSE, texte LGPL…) sont affichés en texte brut.
/// </summary>
public partial class LicensesWindow : Window
{
    private static readonly (string LabelKey, Func<string> Resource)[] Documents =
    {
        ("lic.tab.project", () => "LICENSE"),
        ("lic.tab.notices", () => Loc.Language == "fr" ? "THIRD_PARTY_NOTICES.md" : "THIRD_PARTY_NOTICES.en.md"),
        ("lic.tab.lgpl", () => "LGPL-2.1.txt"),
        ("lic.tab.dotnet", () => "DOTNET-THIRD-PARTY-NOTICES.txt"),
    };

    private int _selected = 1;   // ouvre sur « Composants et obligations »

    public LicensesWindow()
    {
        InitializeComponent();
        BuildTabs();
        Loc.LanguageChanged += BuildTabs;
        Closed += (_, _) => Loc.LanguageChanged -= BuildTabs;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowEffects.DarkTitleBar(this);
    }

    private void BuildTabs()
    {
        Tabs.Children.Clear();
        for (var i = 0; i < Documents.Length; i++)
        {
            var index = i;
            var tab = new RadioButton
            {
                Content = Loc.T(Documents[i].LabelKey),
                GroupName = "docs",
                Style = (Style)FindResource("ChipRadio"),
                IsChecked = i == _selected,
            };
            tab.Checked += (_, _) => { _selected = index; Show(Documents[index].Resource()); };
            Tabs.Children.Add(tab);
        }
        Show(Documents[_selected].Resource());
    }

    private static bool IsMarkdown(string resource) =>
        resource.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || resource.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

    private void Show(string resource)
    {
        using var s = typeof(LicensesWindow).Assembly.GetManifestResourceStream(resource);
        var text = s == null ? Loc.T("lic.missing", resource) : new StreamReader(s).ReadToEnd();

        if (s != null && IsMarkdown(resource))
        {
            try
            {
                MdViewer.Document = MarkdownRenderer.Render(text);
                MdViewer.Visibility = Visibility.Visible;
                Viewer.Visibility = Visibility.Collapsed;
                MdViewer.ScrollToHome();
                return;
            }
            catch
            {
                // Rendu impossible : on retombe sur le texte brut plutôt que d'échouer.
            }
        }

        // Texte brut (fichiers non Markdown, ou repli si le rendu a échoué).
        Viewer.Text = text;
        Viewer.Visibility = Visibility.Visible;
        MdViewer.Visibility = Visibility.Collapsed;
        Viewer.ScrollToHome();
    }
}
