namespace Prospect.Core.Modpacks;

/// <summary>
/// Le manifest lu (seul ou depuis une archive) n'est pas exploitable : JSON invalide, schéma non
/// pris en charge, ou champ obligatoire manquant/vide. Toujours un message directement montrable à
/// l'utilisateur, jamais une <see cref="System.Text.Json.JsonException"/> nue.
/// </summary>
public sealed class ModpackManifestInvalidException : Exception
{
    /// <summary>Construit l'erreur avec un message déjà clair pour l'utilisateur.</summary>
    public ModpackManifestInvalidException(string message)
        : base(message)
    {
    }

    /// <summary>Construit l'erreur en conservant la cause interne (typiquement une erreur de parsing JSON).</summary>
    public ModpackManifestInvalidException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// La source d'import (fichier choisi par l'utilisateur) n'est ni un manifest JSON ni une archive
/// zip contenant un manifest.
/// </summary>
public sealed class ModpackSourceInvalidException : Exception
{
    /// <summary>Construit l'erreur avec un message déjà clair pour l'utilisateur.</summary>
    public ModpackSourceInvalidException(string message)
        : base(message)
    {
    }

    /// <summary>Construit l'erreur en conservant la cause interne.</summary>
    public ModpackSourceInvalidException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}