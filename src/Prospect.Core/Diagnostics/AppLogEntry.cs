using Prospect.Core.Common;

namespace Prospect.Core.Diagnostics;

/// <summary>
/// Une ligne du journal d'application, telle que la page Journaux l'affiche : le texte brut, et le
/// niveau QUAND il a pu être relu.
/// </summary>
/// <remarks>
/// Le niveau est nullable et ce n'est pas un oubli. Une ligne écrite par <see cref="FileAppLog"/>
/// porte son étiquette, mais un journal réel contient aussi ce qui a été appendu autrement : la
/// suite d'une exception sur plusieurs lignes, ou un fichier tronqué en son milieu par le plafond
/// de taille. Ces lignes-là s'affichent telles quelles, sans teinte, plutôt que d'être rangées de
/// force dans un niveau inventé.
/// </remarks>
/// <param name="Text">Ligne complète, horodatage et étiquette compris, telle qu'elle est sur disque.</param>
/// <param name="Level">Niveau relu de l'étiquette, ou <see langword="null"/> si la ligne n'en porte pas.</param>
public sealed record AppLogEntry(string Text, AppLogLevel? Level);