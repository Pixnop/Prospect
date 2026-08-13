using System.IO.Abstractions;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

using Microsoft.Extensions.DependencyInjection;

using Prospect.Core.Auth;
using Prospect.Core.Settings;
using Prospect.Desktop.Services;
using Prospect.Desktop.ViewModels.Shell;

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

        // Même raison, même moment, pour l'autre valeur qui dépend d'un réglage pas encore lu : les
        // textes. Le français est la valeur de démarrage sûre (voix d'origine du produit et repli
        // documenté de toute langue inconnue), remplacé aussitôt par LanguageService.ApplyStartupLanguage
        // si le réglage dit l'anglais — avant qu'aucune vue n'existe, donc avant qu'aucun
        // {StaticResource} ne se résolve. Sans elle, un test headless (qui ne passe jamais par
        // OnFrameworkInitializationCompleted, voir TestServiceProviderFactory) n'aurait aucun
        // dictionnaire de textes du tout.
        LanguageService.MergeStringsDictionary(this, ProspectSettings.French);
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

            // Même contrainte d'ordre que le thème, en plus stricte : la langue doit être posée
            // avant la première vue, parce qu'un {StaticResource} se résout à la construction du
            // contrôle et ne se relit jamais. C'est aussi la seule fixation de UiText de toute la
            // durée de vie du processus (voir sa remarque sur l'entorse assumée).
            _serviceProvider.GetRequiredService<LanguageService>().ApplyStartupLanguage();

            // Même moment, même raison, même blocage assumé : la session de compte doit être relue
            // avant la première fenêtre pour que la section Comptes et la checklist de premier
            // lancement montrent tout de suite l'état réel. Une session absente ou illisible ne lève
            // jamais, elle laisse simplement l'application déconnectée (voir ISecretStore.LoadAsync).
            _serviceProvider.GetRequiredService<VsAccountService>().LoadAsync().GetAwaiter().GetResult();

            // Résolu pour son abonnement, pas pour sa valeur : sans cette ligne, personne n'oublie
            // l'état par slug d'une instance supprimée (voir DeletedInstanceStateCleaner).
            _serviceProvider.GetRequiredService<DeletedInstanceStateCleaner>();

            desktop.MainWindow = _serviceProvider.GetRequiredService<MainWindow>();

            // Après la fenêtre : l'écran de premier lancement s'affiche sur le panneau modal du
            // shell, qui a besoin d'exister. Jamais un effet de bord du constructeur de
            // ShellViewModel (voir sa docstring) : les tests headless qui résolvent ce ViewModel
            // sans passer par ce chemin ne l'appellent jamais.
            _serviceProvider.GetRequiredService<ShellViewModel>().ShowFirstRunIfNeeded();

            desktop.ShutdownRequested += (_, _) => _serviceProvider?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();

        // Panneau de diagnostics (F12), uniquement dans les builds Debug.
#if DEBUG
        this.AttachDevTools();
#endif
    }
}