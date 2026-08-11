namespace Prospect.Core.Storage;

/// <summary>
/// Levée par <see cref="JsonFileStore"/> quand un fichier existe mais que son contenu ne peut
/// pas être désérialisé (JSON invalide, structure inattendue...). Porte le chemin du fichier
/// fautif en clair via <see cref="Path"/>, pour que l'appelant puisse le signaler précisément
/// plutôt que de propager une <see cref="System.Text.Json.JsonException"/> nue qui ne dit pas
/// quel fichier est en cause.
/// </summary>
public sealed class CorruptedFileException : Exception
{
    /// <summary>Constructeur standard, sans contexte de fichier particulier.</summary>
    public CorruptedFileException()
        : base("Le fichier contient du JSON invalide ou corrompu.")
    {
        Path = string.Empty;
    }

    /// <summary>Constructeur standard, avec un message personnalisé.</summary>
    public CorruptedFileException(string message)
        : base(message)
    {
        Path = string.Empty;
    }

    /// <summary>
    /// Construit l'exception pour un fichier précis, avec l'exception de désérialisation
    /// d'origine comme cause interne.
    /// </summary>
    public CorruptedFileException(string path, Exception innerException)
        : base($"Le fichier '{path}' contient du JSON invalide ou corrompu.", innerException)
    {
        Path = path;
    }

    /// <summary>
    /// Chemin du fichier dont le contenu n'a pas pu être désérialisé. Vide si l'exception a été
    /// construite via un des constructeurs standard, non utilisés par <see cref="JsonFileStore"/>.
    /// </summary>
    public string Path { get; }
}