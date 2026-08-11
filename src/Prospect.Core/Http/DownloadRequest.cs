namespace Prospect.Core.Http;

/// <summary>
/// Un téléchargement à effectuer : quoi, depuis où, sous quel nom, et avec quelle empreinte
/// attendue.
/// </summary>
/// <param name="DisplayName">Libellé montré à l'utilisateur dans le popover Téléchargements.</param>
/// <param name="FileName">
/// Nom du fichier dans <c>cache/downloads/</c>. Volontairement un nom simple : il vient d'un
/// document distant, un chemin relatif y serait une porte ouverte à l'écriture hors du cache.
/// </param>
/// <param name="Mirrors">Miroirs par ordre de préférence. Le suivant prend le relais si le précédent flanche.</param>
/// <param name="ExpectedMd5">Empreinte MD5 hexadécimale attendue, ou <see langword="null"/> si la source n'en fournit pas.</param>
public sealed record DownloadRequest(
    string DisplayName,
    string FileName,
    IReadOnlyList<Uri> Mirrors,
    string? ExpectedMd5 = null)
{
    /// <summary>
    /// Valide la demande : nom de fichier non vide et sans composante de chemin, au moins un
    /// miroir. Appelée par le gestionnaire avant toute écriture disque.
    /// </summary>
    /// <exception cref="ArgumentException">Nom de fichier inexploitable, ou aucun miroir.</exception>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(FileName);

        if (Path.GetFileName(FileName) != FileName || FileName is "." or "..")
        {
            throw new ArgumentException($"'{FileName}' n'est pas un nom de fichier simple : un téléchargement ne peut pas écrire hors du cache.", nameof(FileName));
        }

        if (Mirrors is null || Mirrors.Count == 0)
        {
            throw new ArgumentException("Un téléchargement a besoin d'au moins un miroir.", nameof(Mirrors));
        }
    }
}