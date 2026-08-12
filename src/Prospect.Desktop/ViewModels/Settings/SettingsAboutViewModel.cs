using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Prospect.Core.Common;

namespace Prospect.Desktop.ViewModels.Settings;

/// <summary>
/// Section À propos de l'écran Réglages : version de l'app lue de l'assembly (jamais codée en dur,
/// toujours ce que <c>Directory.Build.props</c> a réellement produit ce build-ci), licence et lien
/// vers le dépôt GitHub. Pas de bouton « Rechercher une mise à jour » : la maquette en propose un,
/// mais aucun mécanisme de mise à jour n'existe encore côté Core, l'afficher promettrait quelque
/// chose que l'app ne fait pas.
/// </summary>
public sealed partial class SettingsAboutViewModel : ObservableObject
{
    private const string GitHubRepositoryUrl = "https://github.com/Pixnop/Prospect";

    private readonly IExternalUrlOpener _urlOpener;

    public SettingsAboutViewModel(IExternalUrlOpener urlOpener)
    {
        ArgumentNullException.ThrowIfNull(urlOpener);

        _urlOpener = urlOpener;
        AppVersion = typeof(SettingsAboutViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "?";
    }

    /// <summary>Version complète (avec suffixe de pré-version éventuel), affichée en mono.</summary>
    public string AppVersion { get; }

    /// <summary>Licence du dépôt (voir LICENSE et README à la racine du projet).</summary>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Propriété liée en XAML depuis l'instance du DataContext (x:DataType) : rester une " +
            "propriété d'instance est le contrat attendu par le binding, même si la valeur ne dépend d'aucun " +
            "champ aujourd'hui.")]
    public string License => "GPL-3.0";

    [RelayCommand]
    private Task<bool> OpenGitHubAsync() => _urlOpener.OpenAsync(new Uri(GitHubRepositoryUrl));
}