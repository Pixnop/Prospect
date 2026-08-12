using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Prospect.Core.Auth;

/// <summary>
/// Contexte source-gen du domaine Auth : la réponse de l'endpoint de connexion, la session
/// persistée (<c>session.json</c>) et le document <c>clientsettings.json</c> du jeu, lu et réécrit
/// comme un <see cref="JsonObject"/> pour n'en toucher que les huit clés du contrat.
/// </summary>
/// <remarks>
/// Interne, comme les DTO qu'il expose. Le camelCase ne s'applique qu'à <see cref="VsSession"/>,
/// pour que <c>session.json</c> ressemble aux autres fichiers de Prospect : les noms de la réponse
/// du service sont imposés de l'extérieur et portés un par un par des attributs explicites, et les
/// clés du <c>clientsettings.json</c> du jeu sont écrites à la main dans le
/// <see cref="ClientSettingsSessionWriter"/>, aucune convention ne les touche.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(VsGameLoginResponseDto))]
[JsonSerializable(typeof(VsSession))]
[JsonSerializable(typeof(JsonObject))]
internal sealed partial class VsAuthJsonContext : JsonSerializerContext;