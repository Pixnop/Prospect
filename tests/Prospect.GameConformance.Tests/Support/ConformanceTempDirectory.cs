namespace Prospect.GameConformance.Tests.Support;

/// <summary>
/// Répertoire temporaire réel (vrai système de fichiers, pas <c>MockFileSystem</c>), supprimé
/// récursivement à la fin du test. Cet étage confronte Prospect au moteur réel : simuler le
/// système de fichiers n'aurait aucun sens ici, voir le README du projet.
/// </summary>
internal sealed class ConformanceTempDirectory : IDisposable
{
    public ConformanceTempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "prospect-conformance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>Chemin absolu du répertoire, déjà créé.</summary>
    public string Path { get; }

    /// <summary>Supprime le répertoire, au mieux : un nettoyage raté ne doit jamais faire échouer un test déjà passé.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}