using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Prospect.Core.Auth;

/// <summary>
/// Contexte source-gen du domaine Auth : la réponse de l'endpoint de connexion, la session
/// persistée (<c>session.json</c>) et le document <c>clientsettings.json</c> du jeu, lu et réécrit
/// comme un <see cref="JsonObject"/> pour n'en toucher que les huit clés du contrat.
/// </summary>
/// <remarks>
/// Interne comme les DTO qu'il expose, et sans <c>PropertyNamingPolicy</c> : les noms de champs
/// sont ici imposés par deux contrats extérieurs (le service d'authentification, puis le jeu), pas
/// choisis par Prospect. Chaque nom est donc écrit explicitement à l'endroit qui le porte plutôt
/// que dérivé d'une convention.
/// </remarks>
[JsonSerializable(typeof(VsGameLoginResponseDto))]
[JsonSerializable(typeof(VsSession))]
[JsonSerializable(typeof(JsonObject))]
internal sealed partial class VsAuthJsonContext : JsonSerializerContext;
