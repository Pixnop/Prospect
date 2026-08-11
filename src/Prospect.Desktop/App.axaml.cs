using System.IO.Abstractions;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Microsoft.Extensions.DependencyInjection;

namespace Prospect.Desktop;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            CompositionRoot.ConfigureServices(services, new FileSystem());
            _serviceProvider = services.BuildServiceProvider();

            desktop.MainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            desktop.ShutdownRequested += (_, _) => _serviceProvider?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();

        // Panneau de diagnostics (F12), uniquement dans les builds Debug.
#if DEBUG
        this.AttachDevTools();
#endif
    }
}