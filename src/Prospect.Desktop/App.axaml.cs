using System.IO.Abstractions;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Settings;
using Prospect.Desktop.Services;

namespace Prospect.Desktop;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Valeur de démarrage sûre, avant que le réglage persisté ne soit lu : OnFrameworkInitializationCompleted
        // l'écrase aussitôt via ThemeService.ApplyStartupTheme, une fois SettingsService.LoadAsync
        // terminé. Sans elle, la toute première mise en page (et tout test headless, qui ne passe
        // jamais par OnFrameworkInitializationCompleted — voir TestServiceProviderFactory) n'aurait
        // aucune variante déterministe tant que rien d'autre ne l'a posée : la plateforme headless
        // rapporte Clair par défaut, à l'opposé du sombre historique de l'app.
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            CompositionRoot.ConfigureServices(services, new FileSystem());
            _serviceProvider = services.BuildServiceProvider();

            // Ordre déterminant : les réglages doivent être lus, et le thème appliqué, AVANT que la
            // première fenêtre ne se construise (ThemeService.ApplyStartupTheme, voir sa docstring
            // sur pourquoi ce n'est jamais un effet de bord automatique de la résolution DI).
            // LoadAsync().GetAwaiter().GetResult() est un blocage volontaire : on est encore avant le
            // démarrage de la boucle de messages Avalonia, donc sans risque d'interblocage, et la
            // lecture d'un unique petit fichier JSON est quasi instantanée.
            var settings = _serviceProvider.GetRequiredService<SettingsService>();
            settings.LoadAsync().GetAwaiter().GetResult();
            _serviceProvider.GetRequiredService<ThemeService>().ApplyStartupTheme();

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