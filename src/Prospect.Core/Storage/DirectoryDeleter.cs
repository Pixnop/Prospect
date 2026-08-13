using System.IO.Abstractions;

namespace Prospect.Core.Storage;

/// <summary>
/// Avancement d'une suppression récursive : fichiers effacés sur fichiers à effacer.
/// </summary>
/// <param name="DeletedFiles">Fichiers déjà effacés.</param>
/// <param name="TotalFiles">Fichiers relevés au départ.</param>
public sealed record DirectoryDeleteProgress(int DeletedFiles, int TotalFiles)
{
    /// <summary>Avancement entre 0 et 1. Un dossier vide est fini d'emblée, pas éternellement à zéro.</summary>
    public double Ratio => TotalFiles <= 0 ? 1d : Math.Clamp((double)DeletedFiles / TotalFiles, 0d, 1d);
}

/// <summary>
/// Une suppression récursive qui dure vraiment ne doit rien effacer sans le dire : un dossier de six
/// cents mégaoctets prend des dizaines de secondes, et une fenêtre muette pendant ce temps se lit
/// comme un gel.
/// </summary>
/// <remarks>
/// <para>
/// D'où l'énumération PRÉALABLE : compter les fichiers est rapide (une lecture d'index), les effacer
/// ne l'est pas. C'est ce qui donne un dénominateur, donc une barre déterminée plutôt qu'un rond qui
/// tourne. La suppression finale du dossier balaie ce qui reste, les dossiers vides et tout fichier
/// apparu entre-temps.
/// </para>
/// <para>
/// Cette méthode est SYNCHRONE, et volontairement : <c>System.IO.Abstractions</c> l'est de bout en
/// bout, et attendre un appel synchrone ne le déplace pas d'un thread. C'est à l'appelant de la
/// pousser hors du thread d'interface avec un <see cref="Task.Run(Action)"/> — les deux services qui
/// l'utilisent le font.
/// </para>
/// </remarks>
internal static class DirectoryDeleter
{
    /// <summary>
    /// Efface <paramref name="directory"/> et tout ce qu'il contient, en publiant son avancement.
    /// Un dossier absent n'est pas une erreur : il n'y a rien à faire.
    /// </summary>
    /// <exception cref="DirectoryDeleteFailedException">Il reste quelque chose sur le disque.</exception>
    public static void Delete(IFileSystem fileSystem, string directory, IProgress<DirectoryDeleteProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrEmpty(directory);

        if (!fileSystem.Directory.Exists(directory))
        {
            progress?.Report(new DirectoryDeleteProgress(0, 0));

            return;
        }

        try
        {
            var files = fileSystem.Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            progress?.Report(new DirectoryDeleteProgress(0, files.Length));

            var lastPercent = 0;
            for (var index = 0; index < files.Length; index++)
            {
                fileSystem.File.Delete(files[index]);

                // Un rapport par point de pourcentage : une instance peut porter des dizaines de
                // milliers de fichiers, et chaque rapport traverse le dispatcher de l'interface.
                var percent = (index + 1) * 100 / files.Length;
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    progress?.Report(new DirectoryDeleteProgress(index + 1, files.Length));
                }
            }

            // Les dossiers vides restants, et tout fichier apparu depuis l'énumération.
            fileSystem.Directory.Delete(directory, recursive: true);
            progress?.Report(new DirectoryDeleteProgress(files.Length, files.Length));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DirectoryDeleteFailedException(directory, exception);
        }
    }
}

/// <summary>
/// Il reste des fichiers sur le disque. Le message ne cherche pas à dire POURQUOI : un fichier
/// verrouillé par le jeu, un dossier synchronisé, un antivirus qui tient une archive ouverte
/// produisent des exceptions différentes selon l'OS, et le seul fait utile à l'utilisateur est le
/// même dans les trois cas.
/// </summary>
public sealed class DirectoryDeleteFailedException : Exception
{
    /// <summary>Construit l'erreur.</summary>
    public DirectoryDeleteFailedException(string directory, Exception innerException)
        : base($"La suppression de « {directory} » n'a pas pu aller jusqu'au bout.", innerException)
        => Directory = directory;

    /// <summary>Construit l'erreur sans cause.</summary>
    public DirectoryDeleteFailedException(string message)
        : base(message) => Directory = string.Empty;

    /// <summary>Construit l'erreur sans cause ni message.</summary>
    public DirectoryDeleteFailedException()
        : this("La suppression n'a pas pu aller jusqu'au bout.")
    {
    }

    /// <summary>Dossier dont il reste quelque chose.</summary>
    public string Directory { get; }
}