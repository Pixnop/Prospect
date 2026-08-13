namespace Prospect.Core.Common;

/// <summary>
/// Port vers la culture d'interface du système, celle dans laquelle l'utilisateur s'attend à lire
/// (<c>CultureInfo.CurrentUICulture</c>). Sert une seule décision : la langue par défaut d'une
/// installation neuve, quand aucun <c>prospect.json</c> n'existe encore (voir
/// <see cref="Settings.SettingsService.LoadAsync"/>).
/// </summary>
/// <remarks>
/// Un port à part plutôt qu'une propriété de plus sur <see cref="Storage.IAppEnvironment"/> : ce
/// dernier est explicitement le port de « ce dont dépend l'EMPLACEMENT des données »
/// (variables d'environnement, dossiers spéciaux, OS courant), injecté dans <c>AppPaths</c> et
/// <c>VslPaths</c> où une culture n'a rien à faire. Même découpage que
/// <see cref="IClock"/> ou <see cref="IProcessRunner"/> : un port, un effet de bord, un seul
/// membre.
/// </remarks>
public interface IUiCulture
{
    /// <summary>
    /// Nom de la culture d'interface courante, au format BCP 47 (« fr-FR », « en-US », ou un nom
    /// neutre comme « fr »). Jamais <see langword="null"/> ; la culture invariante rend une chaîne
    /// vide, ce qui est une valeur exploitable et non une absence.
    /// </summary>
    string Name { get; }
}