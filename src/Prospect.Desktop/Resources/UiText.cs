using Prospect.Core.Settings;

namespace Prospect.Desktop.Resources;

/// <summary>
/// Textes UI produits par du code C# (ViewModels) plutôt que lus depuis un binding XAML direct :
/// messages de validation, confirmations qui nomment une instance, textes de toast interpolés.
/// Existe à côté de <c>Strings.fr.axaml</c>/<c>Strings.en.axaml</c> (voir leur commentaire
/// d'en-tête pour le partage des rôles) : chaque texte UI n'est défini qu'à un seul des deux
/// endroits.
/// </summary>
/// <remarks>
/// <para>
/// Façade statique au-dessus d'une <see cref="UiTextTable"/> par langue, ce qui laisse les appels
/// existants (<c>UiText.Mods.PlanTitle(...)</c>) exactement tels qu'ils étaient quand ce fichier
/// portait des constantes.
/// </para>
/// <para>
/// ENTORSE ASSUMÉE au « jamais d'état statique muable » de docs/architecture.md, et la seule du
/// projet. La justification tient en deux points. D'abord, la langue est fixée UNE FOIS, avant que
/// la moindre vue ne se construise (voir <c>Services.LanguageService.ApplyStartupLanguage</c>), et
/// ne rebascule jamais à chaud : ce n'est donc pas un état qui varie pendant l'exécution, c'est une
/// constante décidée au démarrage. Ensuite, l'alternative (injecter la table dans les quelque
/// trente ViewModels qui écrivent du texte) ajouterait une dépendance de constructeur partout
/// pour une valeur que personne ne peut changer, et rendrait chaque ViewModel plus lourd à
/// construire en test sans rien rendre plus vrai. Le garde-fou est
/// <see cref="Fix(string)"/> : une deuxième fixation lève, ce qui rend structurellement impossible
/// qu'un chemin de code se mette à changer la langue en cours de route.
/// </para>
/// <para>
/// Le harnais de test épingle le français par <see cref="ResetForTests"/> (visible via
/// <c>InternalsVisibleTo</c>), indépendamment de la culture de la machine.
/// </para>
/// </remarks>
internal static class UiText
{
    private static readonly Lock Gate = new();

    private static UiTextTable _table = new FrenchUiText();
    private static bool _fixed;

    /// <summary>Langue actuellement servie par cette façade.</summary>
    internal static string Language => _table.Language;

    internal static ShellText Shell => _table.Shell;

    internal static WizardText Wizard => _table.Wizard;

    internal static DialogsText Dialogs => _table.Dialogs;

    internal static ToastsText Toasts => _table.Toasts;

    internal static HomeText Home => _table.Home;

    internal static DownloadsText Downloads => _table.Downloads;

    internal static VersionsText Versions => _table.Versions;

    internal static BrokenInstancesText BrokenInstances => _table.BrokenInstances;

    internal static InstanceText Instance => _table.Instance;

    internal static AccountText Account => _table.Account;

    internal static ModsText Mods => _table.Mods;

    internal static ModpacksText Modpacks => _table.Modpacks;

    internal static MigrationText Migration => _table.Migration;

    internal static SettingsText Settings => _table.Settings;

    internal static FirstRunText FirstRun => _table.FirstRun;

    internal static TimeText Time => _table.Time;

    internal static LogsText Logs => _table.Logs;

    /// <summary>
    /// La table correspondant à <paramref name="language"/>. Toute valeur inconnue rend la table
    /// française, le repli documenté de <see cref="ProspectSettings.NormalizeLanguage"/>.
    /// </summary>
    internal static UiTextTable TableFor(string? language)
        => ProspectSettings.NormalizeLanguage(language) == ProspectSettings.English
            ? new EnglishUiText()
            : new FrenchUiText();

    /// <summary>
    /// Fixe la langue de la façade, UNE SEULE FOIS, avant la construction de la première vue.
    /// Appelé par <c>Services.LanguageService.ApplyStartupLanguage</c>, jamais ailleurs.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// La langue a déjà été fixée. Ce n'est pas un cas à rattraper : deux fixations veulent dire
    /// qu'un chemin de code cherche à retraduire l'application en marche, ce que cette conception
    /// exclut (voir la remarque de la classe et docs/architecture.md).
    /// </exception>
    internal static void Fix(string language)
    {
        lock (Gate)
        {
            if (_fixed)
            {
                throw new InvalidOperationException(
                    $"La langue de l'interface est déjà fixée sur « {_table.Language} » : elle se choisit une seule fois au démarrage, jamais à chaud.");
            }

            _table = TableFor(language);
            _fixed = true;
        }
    }

    /// <summary>
    /// Repose la langue et rouvre la garde, pour le harnais de test uniquement : chaque test qui
    /// exerce l'anglais rétablit ensuite le français, exactement comme les tests de thème
    /// rétablissent la variante sombre.
    /// </summary>
    internal static void ResetForTests(string language)
    {
        lock (Gate)
        {
            _table = TableFor(language);
            _fixed = false;
        }
    }
}