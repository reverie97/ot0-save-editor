using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Ot0Gui;

public partial class MainWindow : Window
{
    readonly MainViewModel vm = new();

    public MainWindow(string? file, string? screenshotDir)
    {
        InitializeComponent();
        DataContext = vm;

        Loaded += async (_, _) =>
        {
            await vm.Start(file);
            if (screenshotDir is not null)
            {
                await SaveScreenshots(screenshotDir);
                Close();
            }
        };
        Closed += (_, _) => vm.Dispose();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (vm.Dirty && MessageBox.Show("Discard unsaved changes and quit?", "OT0 Save Editor",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            e.Cancel = true;
        base.OnClosing(e);
    }

    void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // One PNG per tab, rendered from the visual tree at 2x. Used for the README.
    async Task SaveScreenshots(string dir)
    {
        Directory.CreateDirectory(dir);
        string[] names = ["party", "items"];
        for (int i = 0; i < names.Length; i++)
        {
            Tabs.SelectedIndex = i;
            if (i == 0) PartyGrid.SelectedIndex = 0;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Root.UpdateLayout();

            var bmp = new RenderTargetBitmap((int)(Root.ActualWidth * 2), (int)(Root.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
            bmp.Render(Root);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            using var file = File.Create(Path.Combine(dir, names[i] + ".png"));
            png.Save(file);
        }
    }
}
