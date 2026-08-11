namespace Prospect.Core.GameVersions;

/// <summary>
/// Un fichier téléchargeable du catalogue, pour une version et une plateforme données.
/// </summary>
/// <param name="PlatformKey">Clé de plateforme du catalogue (voir <see cref="GamePlatforms"/>).</param>
/// <param name="FileName">Nom de fichier annoncé, réutilisé tel quel dans <c>cache/downloads/</c>.</param>
/// <param name="DisplaySize">
/// Taille telle que le catalogue l'écrit, une chaîne lisible du type « 590.5 MB » et non un nombre
/// d'octets (docs/research/vslauncher-et-distribution.md, section b). Elle sert à l'affichage
/// uniquement : le nombre d'octets exact vient d'un <c>Content-Length</c> obtenu au moment du
/// téléchargement, jamais d'une conversion approximative de cette chaîne.
/// </param>
/// <param name="Md5">Empreinte MD5 hexadécimale annoncée par le catalogue.</param>
/// <param name="CdnUrl">Miroir principal (edge BunnyCDN).</param>
/// <param name="LocalUrl">Second miroir (origine nginx du portail officiel), utilisé en repli.</param>
/// <param name="IsLatest">Drapeau <c>latest</c> du catalogue pour cette plateforme.</param>
public sealed record GameVersionAsset(
    string PlatformKey,
    string FileName,
    string DisplaySize,
    string Md5,
    Uri CdnUrl,
    Uri LocalUrl,
    bool IsLatest)
{
    /// <summary>
    /// Les deux miroirs dans l'ordre de préférence. Le repli automatique sur le second est un
    /// manque documenté de VS Launcher, qui n'utilisait jamais <c>urls.local</c>
    /// (docs/research/vslauncher-et-distribution.md, implication 11).
    /// </summary>
    public IReadOnlyList<Uri> Mirrors { get; } = [CdnUrl, LocalUrl];
}