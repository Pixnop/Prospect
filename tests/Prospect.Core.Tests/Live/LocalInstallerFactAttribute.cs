namespace Prospect.Tests.Live;

/// <summary>
/// Un test qui a besoin d'un VRAI installeur Windows de Vintage Story, désigné par la variable
/// d'environnement <see cref="InstallerPathVariable"/>. Ignoré, avec la marche à suivre, quand elle
/// n'est pas posée ou ne désigne rien.
/// </summary>
/// <remarks>
/// <para>
/// Cet opt-in-là s'ajoute à celui de <see cref="LiveOptIn"/> au lieu de le remplacer, et la raison
/// est une question de politesse envers un service tiers. Le reste de l'étage live tient dans
/// quelques requêtes espacées, et <c>GameArchiveLayoutLiveTests</c> va jusqu'à ne demander que
/// 300 Ko en tête d'archive par requête de plage plutôt que les 590 Mo entiers. Un installeur
/// Windows pèse 570 Mo et ne se lit pas par morceaux : ses en-têtes sont à la fin du fichier, ses
/// données au début. Le télécharger chaque dimanche depuis le CDN d'un petit studio serait
/// exactement ce que cette doctrine cherche à éviter.
/// </para>
/// <para>
/// Le fichier ne manque à personne de toute façon : Prospect garde ses téléchargements dans
/// <c>cache/downloads/</c>, donc quiconque a installé une version Windows en a déjà un sous la
/// main.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LocalInstallerFactAttribute : FactAttribute
{
    /// <summary>Variable qui désigne l'installeur à lire.</summary>
    public const string InstallerPathVariable = "PROSPECT_LIVE_INSTALLER";

    /// <summary>Construit l'attribut, en lisant les deux opt-in immédiatement.</summary>
    public LocalInstallerFactAttribute() => Skip = LiveOptIn.SkipReason ?? MissingInstallerReason();

    /// <summary>Chemin de l'installeur désigné, quand il en existe un.</summary>
    public static string? InstallerPath
    {
        get
        {
            var path = Environment.GetEnvironmentVariable(InstallerPathVariable);

            return !string.IsNullOrEmpty(path) && File.Exists(path) ? path : null;
        }
    }

    private static string? MissingInstallerReason() => InstallerPath is not null
        ? null
        : $"Ignoré : ce test lit un vrai installeur Windows de Vintage Story, qu'il ne télécharge pas "
            + $"(570 Mo). Pour l'exécuter, désignez-en un : {InstallerPathVariable}=/chemin/vers/"
            + "vs_install_win-x64_<version>.exe PROSPECT_LIVE=1 dotnet test. Prospect garde les siens "
            + "dans cache/downloads/.";
}