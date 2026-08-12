namespace Prospect.Core.Settings;

/// <summary>
/// Préférences de téléchargement persistées dans <c>prospect.json</c> : aujourd'hui uniquement le
/// parallélisme, branché sur <c>Prospect.Core.Http.DownloadOptions.MaxParallelDownloads</c> à la
/// construction du <c>DownloadManager</c> partagé (voir <c>CompositionRoot</c>). Le reste des champs
/// de <c>DownloadOptions</c> (délai d'inactivité, taille de tampon...) n'est pas exposé : rien ne le
/// consomme encore côté réglages.
/// </summary>
public sealed record DownloadPreferences
{
    /// <summary>Borne basse raisonnable : en dessous, les téléchargements deviennent strictement séquentiels.</summary>
    public const int MinParallelDownloads = 1;

    /// <summary>
    /// Borne haute raisonnable : au-delà, le gain de débit réel est marginal et le risque de
    /// saturer une connexion modeste (ou de heurter une limite de connexions simultanées côté
    /// serveur) l'emporte.
    /// </summary>
    public const int MaxParallelDownloadsCeiling = 8;

    /// <summary>Les seuls choix proposés par le sélecteur des Réglages (design : « N téléchargements simultanés »).</summary>
    public static readonly IReadOnlyList<int> AllowedChoices = [1, 2, 4, 8];

    /// <summary>Téléchargements simultanés. Aligné sur <c>DownloadOptions.Default</c> (2) tant que rien n'a été choisi.</summary>
    public int MaxParallelDownloads { get; init; } = 2;

    public static DownloadPreferences Default { get; } = new();

    /// <summary>
    /// Copie bornée à <see cref="MinParallelDownloads"/>–<see cref="MaxParallelDownloadsCeiling"/>.
    /// Le sélecteur des Réglages ne propose que des valeurs déjà valides
    /// (<see cref="AllowedChoices"/>), mais le modèle se protège aussi lui-même : un
    /// <c>prospect.json</c> modifié à la main ou écrit par une version future ne doit jamais faire
    /// démarrer un <c>DownloadManager</c> hors bornes.
    /// </summary>
    public DownloadPreferences Clamped() => this with
    {
        MaxParallelDownloads = Math.Clamp(MaxParallelDownloads, MinParallelDownloads, MaxParallelDownloadsCeiling),
    };
}