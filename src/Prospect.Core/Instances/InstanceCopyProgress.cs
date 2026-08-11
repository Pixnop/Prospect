namespace Prospect.Core.Instances;

/// <summary>
/// Progression de la copie de <c>data/</c> effectuée par <see cref="InstanceService.DuplicateAsync"/>,
/// rapportée via <see cref="IProgress{T}"/>. Granularité au fichier : c'est aussi l'unité à
/// laquelle l'annulation est vérifiée.
/// </summary>
/// <param name="FilesCopied">Nombre de fichiers déjà copiés, celui-ci inclus.</param>
/// <param name="TotalFiles">Nombre total de fichiers à copier.</param>
public sealed record InstanceCopyProgress(int FilesCopied, int TotalFiles);