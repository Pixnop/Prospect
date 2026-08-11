using System.IO.Abstractions;
using System.Security.Cryptography;

namespace Prospect.Core.Modpacks;

/// <summary>
/// Calcul de l'empreinte SHA-256 d'un fichier, en flux et par blocs, pour ne jamais charger un zip
/// de mod entier en mémoire. Miroir de <see cref="Prospect.Core.Http.Md5Checksum"/>, dans ce
/// domaine plutôt que dans <c>Http</c> parce que c'est une contrainte propre au manifest de
/// modpack : le ModDB n'expose aucune somme de contrôle (docs/research/moddb-api.md), c'est donc
/// Prospect qui calcule et porte celle-ci, à l'export comme à la vérification d'un mod téléchargé
/// à l'import.
/// </summary>
internal static class Sha256Checksum
{
    /// <summary>Empreinte hexadécimale minuscule du fichier <paramref name="path"/>.</summary>
    public static async Task<string> ComputeAsync(IFileSystem fileSystem, string path, CancellationToken cancellationToken, int bufferSize = 81920)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[bufferSize];

        var stream = fileSystem.File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// Compare deux empreintes hexadécimales sans tenir compte de la casse : un manifest écrit à
    /// la main pourrait très bien porter des majuscules.
    /// </summary>
    public static bool Matches(string expected, string actual)
        => string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
}