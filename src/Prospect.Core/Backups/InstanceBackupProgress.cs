namespace Prospect.Core.Backups;

/// <summary>
/// Progression d'une création (<see cref="InstanceBackupService.CreateAsync"/>) ou d'une
/// restauration (<see cref="InstanceBackupService.RestoreAsync"/>) de sauvegarde, rapportée via
/// <see cref="IProgress{T}"/>. Granularité au fichier (zippé ou extrait) : c'est aussi l'unité à
/// laquelle l'annulation est vérifiée, même principe que <see cref="Storage.DirectoryCopyProgress"/>
/// et <see cref="Instances.InstanceCopyProgress"/>.
/// </summary>
/// <param name="FilesProcessed">Nombre de fichiers déjà traités, celui-ci inclus.</param>
/// <param name="TotalFiles">Nombre total de fichiers à traiter.</param>
public sealed record InstanceBackupProgress(int FilesProcessed, int TotalFiles);