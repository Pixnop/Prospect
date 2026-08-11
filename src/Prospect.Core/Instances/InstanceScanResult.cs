namespace Prospect.Core.Instances;

/// <summary>
/// Résultat d'<see cref="IInstanceRepository.ScanAsync"/> : les instances chargées avec succès et,
/// séparément, les dossiers candidats qui n'ont pas pu l'être. Un dossier illisible ne fait jamais
/// échouer le scan dans son ensemble.
/// </summary>
/// <param name="Instances">Instances valides, une entrée par dossier chargé avec succès.</param>
/// <param name="BrokenInstances">Dossiers candidats rejetés, avec leur raison.</param>
public sealed record InstanceScanResult(IReadOnlyList<InstanceRecord> Instances, IReadOnlyList<BrokenInstance> BrokenInstances);