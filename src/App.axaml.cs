using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Sextant.Git;
using Sextant.ViewModels;
using Sextant.Views;

namespace Sextant;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var store = new WorkspaceStore(AppPaths.ConfigDirectory());
            var window = new MainWindow
            {
                DataContext = new MainViewModel(store, store.LoadWorkspace(), store.LoadSettings(), new GitProcessRunner()),
            };
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) =>
            {
                if (window.DataContext is MainViewModel vm)
                    vm.Shutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
