using System.Windows;
using IptvPlayer.Localization;
using IptvPlayer.Services;

namespace IptvPlayer;

public partial class App : Application
{
    public App()
    {
        // Langue de l'interface : anglais par défaut, sinon le choix enregistré (Paramètres).
        Loc.SetLanguage(StateStore.Load().Language);

        DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.Message, Loc.T("error.title"), MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }
}
