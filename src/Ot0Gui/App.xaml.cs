using System.Windows;

namespace Ot0Gui;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --file opens a specific save; --screenshots renders each tab to PNGs and quits
        // (that's how the README images are made).
        string? file = null, shots = null;
        for (int i = 0; i + 1 < e.Args.Length; i++)
        {
            if (e.Args[i] == "--file") file = e.Args[i + 1];
            if (e.Args[i] == "--screenshots") shots = e.Args[i + 1];
        }

        var window = new MainWindow(file, shots);
        if (shots is null)
        {
            window.Show();
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -4000;
            window.Top = -4000;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Show();
        }
    }
}
