using System.IO.Abstractions;

namespace Prospect.Core.ModDb;

/// <summary>
/// Comment un mod installé est marqué activé ou désactivé sur le disque. Isolé derrière une
/// interface pour une raison précise, à ne pas perdre de vue.
/// </summary>
/// <remarks>
/// <para>
/// L'implémentation par défaut (<see cref="DisabledSuffixModStateConvention"/>) désactive un mod en
/// renommant <c>&lt;nom&gt;.zip</c> en <c>&lt;nom&gt;.zip.disabled</c>. Cette convention repose sur
/// une HYPOTHÈSE qui n'a pas encore été validée en jeu réel : que Vintage Story ne charge que les
/// fichiers <c>*.zip</c> de son dossier <c>Mods/</c> et ignore purement et simplement tout autre
/// suffixe. C'est le comportement attendu et c'est ce que fait le monde du modding en général,
/// mais la recherche ne l'a pas vérifié dans le code du jeu, et VS Launcher n'offrait aucun
/// mécanisme équivalent dont on aurait pu s'inspirer.
/// </para>
/// <para>
/// D'où cette interface plutôt qu'un renommage écrit en dur au milieu du repository : si un test
/// en jeu montre que le jeu charge aussi les <c>.zip.disabled</c> (ou plante dessus), il suffit
/// d'écrire une seconde implémentation qui déplace le fichier vers un dossier voisin hors du
/// dataPath, sans toucher au repository, au service d'installation ni à l'UI. C'est pour ça que
/// les méthodes raisonnent en CHEMINS et non en simples noms de fichiers : une convention par
/// dossier reste exprimable.
/// </para>
/// </remarks>
public interface IModStateConvention
{
    /// <summary>Les fichiers du dossier de mods à considérer, activés comme désactivés.</summary>
    /// <param name="fileSystem">Système de fichiers abstrait.</param>
    /// <param name="modsDirectory">Dossier <c>data/Mods/</c> de l'instance.</param>
    IEnumerable<string> EnumerateModFiles(IFileSystem fileSystem, string modsDirectory);

    /// <summary>Vrai si le fichier, à cet emplacement et sous ce nom, est chargé par le jeu.</summary>
    bool IsEnabled(string filePath);

    /// <summary>
    /// Chemin que doit prendre <paramref name="filePath"/> pour être dans l'état demandé. Rend le
    /// même chemin s'il y est déjà.
    /// </summary>
    /// <param name="fileSystem">Système de fichiers abstrait.</param>
    /// <param name="modsDirectory">Dossier <c>data/Mods/</c> de l'instance.</param>
    /// <param name="filePath">Chemin actuel du fichier.</param>
    /// <param name="enabled">État visé.</param>
    string ResolveTargetPath(IFileSystem fileSystem, string modsDirectory, string filePath, bool enabled);

    /// <summary>
    /// Nom stable d'un mod, indépendant de son état. Sert de clé d'affichage et de correspondance
    /// avec la provenance, qui ne doit pas changer quand on désactive un mod.
    /// </summary>
    string GetStableFileName(IFileSystem fileSystem, string filePath);
}

/// <summary>
/// Convention par suffixe : <c>&lt;nom&gt;.zip</c> quand le mod est actif,
/// <c>&lt;nom&gt;.zip.disabled</c> quand il ne l'est pas. Voir la remarque d'
/// <see cref="IModStateConvention"/> pour l'hypothèse sur laquelle elle repose et ce qu'il faudrait
/// faire si elle tombait.
/// </summary>
public sealed class DisabledSuffixModStateConvention : IModStateConvention
{
    /// <summary>Extension d'une archive de mod.</summary>
    public const string ArchiveExtension = ".zip";

    /// <summary>Suffixe ajouté pour sortir un mod du champ de chargement du jeu.</summary>
    public const string DisabledSuffix = ".disabled";

    /// <inheritdoc />
    public IEnumerable<string> EnumerateModFiles(IFileSystem fileSystem, string modsDirectory)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(modsDirectory);

        if (!fileSystem.Directory.Exists(modsDirectory))
        {
            // Un dossier Mods/ absent est une absence normale (instance neuve), pas une erreur.
            return [];
        }

        return fileSystem.Directory
            .EnumerateFiles(modsDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(IsModFile)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public bool IsEnabled(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        return !filePath.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public string ResolveTargetPath(IFileSystem fileSystem, string modsDirectory, string filePath, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var stable = GetStableFileName(fileSystem, filePath);
        var directory = fileSystem.Path.GetDirectoryName(filePath);
        var target = enabled ? stable : stable + DisabledSuffix;

        return string.IsNullOrEmpty(directory) ? target : fileSystem.Path.Combine(directory, target);
    }

    /// <inheritdoc />
    public string GetStableFileName(IFileSystem fileSystem, string filePath)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var name = fileSystem.Path.GetFileName(filePath);

        return name.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^DisabledSuffix.Length]
            : name;
    }

    private static bool IsModFile(string path)
    {
        var name = Path.GetFileName(path);
        var stable = name.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^DisabledSuffix.Length]
            : name;

        return stable.EndsWith(ArchiveExtension, StringComparison.OrdinalIgnoreCase);
    }
}