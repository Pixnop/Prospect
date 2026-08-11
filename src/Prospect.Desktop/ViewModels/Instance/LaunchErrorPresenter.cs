using Prospect.Core.Instances;
using Prospect.Core.Launching;
using Prospect.Core.Runtime;
using Prospect.Desktop.Resources;

namespace Prospect.Desktop.ViewModels.Instance;

/// <summary>Action que l'utilisateur peut prendre pour résoudre une erreur de pré-lancement.</summary>
internal enum LaunchErrorAction
{
    /// <summary>Rien de plus à proposer que le message.</summary>
    None,

    /// <summary>Proposer d'installer la version manquante (envoie vers l'écran Versions).</summary>
    InstallVersion,
}

/// <summary>Ce qu'une erreur de <see cref="GameLauncher.LaunchAsync"/> doit afficher à l'utilisateur.</summary>
internal sealed record LaunchErrorPresentation(string Title, string Message, LaunchErrorAction Action);

/// <summary>
/// Traduit une exception de <see cref="GameLauncher.LaunchAsync"/> en un message présentable et,
/// le cas échéant, une action proposée. Logique partagée entre la carte d'Accueil (toast) et la
/// page de détail (bandeau) : docs/architecture.md exige la même chose des deux surfaces, seule
/// leur façon de l'afficher diffère (voir <see cref="ViewModels.Home.InstanceCardViewModel"/> et
/// <see cref="InstanceDetailViewModel"/>). Classe statique pure, testable sans UI.
/// </summary>
internal static class LaunchErrorPresenter
{
    public static LaunchErrorPresentation Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            GameVersionNotInstalledException => new LaunchErrorPresentation(UiText.Instance.VersionNotInstalledTitle, exception.Message, LaunchErrorAction.InstallVersion),
            RuntimeNotAvailableException => new LaunchErrorPresentation(UiText.Instance.RuntimeMissingTitle, exception.Message, LaunchErrorAction.None),
            MacLaunchNotSupportedException => new LaunchErrorPresentation(UiText.Instance.MacNotSupportedTitle, exception.Message, LaunchErrorAction.None),
            InstanceAlreadyRunningException => new LaunchErrorPresentation(UiText.Instance.AlreadyRunningTitle, exception.Message, LaunchErrorAction.None),
            _ => new LaunchErrorPresentation(UiText.Instance.GenericLaunchErrorTitle, exception.Message, LaunchErrorAction.None),
        };
    }
}