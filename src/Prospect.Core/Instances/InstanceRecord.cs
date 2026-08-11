namespace Prospect.Core.Instances;

/// <summary>
/// Une instance telle qu'exposée par le domaine : son slug de dossier (topologie disque, stable
/// après création) associé à ses métadonnées courantes. <see cref="IInstanceRepository"/> et
/// <see cref="InstanceService"/> échangent systématiquement cette paire plutôt que la seule
/// <see cref="InstanceMetadata"/>, qui ne porte pas le nom de dossier.
/// </summary>
/// <param name="Slug">Nom du dossier sous <c>instances/</c>.</param>
/// <param name="Metadata">Contenu désérialisé d'<c>instance.json</c>.</param>
public sealed record InstanceRecord(string Slug, InstanceMetadata Metadata);