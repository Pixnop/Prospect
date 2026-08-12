namespace Prospect.Core.Migration;

/// <summary>
/// Une entrée de <c>config.json</c> qui n'a pas pu être exploitée (chemin manquant, entrée dont le
/// JSON ne correspond à aucun des champs attendus...), signalée plutôt que bloquante : le parsing
/// continue avec les entrées suivantes, comme les instances et installations de jeu cassées
/// ailleurs dans le Core (<see cref="Prospect.Core.Instances.BrokenInstance"/>,
/// <see cref="Prospect.Core.GameVersions.BrokenGameInstall"/>).
/// </summary>
/// <param name="EntryLabel">Nom de l'entrée si lisible, sinon sa position (ex. <c>installations[2]</c>).</param>
/// <param name="Reason">Raison courte, à destination de l'utilisateur.</param>
public sealed record VslConfigIssue(string EntryLabel, string Reason);

/// <summary>Résultat du parsing d'un <c>config.json</c> de VS Launcher par <see cref="VslConfigParser"/>.</summary>
/// <param name="Installations">Installations exploitables (chemin présent).</param>
/// <param name="GameVersions">Moteurs exploitables (chemin et version présents).</param>
/// <param name="Issues">Entrées ignorées, avec leur raison.</param>
public sealed record VslConfigParseResult(
    IReadOnlyList<VslInstallation> Installations,
    IReadOnlyList<VslGameVersionEntry> GameVersions,
    IReadOnlyList<VslConfigIssue> Issues);