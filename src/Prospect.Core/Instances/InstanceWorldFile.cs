namespace Prospect.Core.Instances;

/// <summary>
/// Un fichier de sauvegarde trouvé sous <c>data/Saves/</c> d'une instance (onglet « Mondes » du
/// design). Reflète uniquement les métadonnées du système de fichiers : Prospect ne lit jamais le
/// format binaire des sauvegardes.
/// </summary>
/// <param name="FileName">Nom du fichier, sans son chemin.</param>
/// <param name="SizeInBytes">Taille du fichier en octets.</param>
/// <param name="LastModifiedUtc">Date de dernière écriture, en UTC.</param>
public sealed record InstanceWorldFile(string FileName, long SizeInBytes, DateTimeOffset LastModifiedUtc);