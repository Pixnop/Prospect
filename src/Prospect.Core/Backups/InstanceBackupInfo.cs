namespace Prospect.Core.Backups;

/// <summary>
/// Une sauvegarde existante d'une instance, telle qu'exposée par
/// <see cref="InstanceBackupService.ListAsync"/> (design : liste « nom, taille, date »). Reflète le
/// fichier zip sur disque, jamais son contenu : <see cref="InstanceBackupService"/> ne rouvre
/// l'archive qu'au moment de la restaurer.
/// </summary>
/// <param name="FileName">Nom du fichier zip, sans son chemin (aussi l'identifiant passé à <see cref="InstanceBackupService.DeleteAsync"/>/<see cref="InstanceBackupService.RestoreAsync"/>).</param>
/// <param name="SizeInBytes">Taille du fichier en octets.</param>
/// <param name="CreatedUtc">Horodatage de création, lu depuis le nom du fichier (posé par <see cref="Common.IClock"/> à la création) avec repli sur la date d'écriture du fichier si le nom ne suit pas la convention attendue.</param>
public sealed record InstanceBackupInfo(string FileName, long SizeInBytes, DateTimeOffset CreatedUtc);