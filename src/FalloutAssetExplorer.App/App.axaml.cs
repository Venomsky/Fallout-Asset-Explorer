using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.App.Views;

namespace FalloutAssetExplorer.App;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new ArchiveBrowserViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
