using System.Windows;

namespace IptvPlayer;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show(e.Exception.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }
}
