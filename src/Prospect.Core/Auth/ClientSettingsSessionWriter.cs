using System.IO.Abstractions;
using System.Text.Json.Nodes;

using Prospect.Core.Storage;

namespace Prospect.Core.Auth;

/// <summary>
/// Écrit la session de compte dans le <c>clientsettings.json</c> du dataPath d'une instance, juste
/// avant que le jeu ne démarre. C'est le seul usage réel du compte dans tout Prospect : le
/// launcher ne s'authentifie jamais lui-même, il pose les huit clés que le JEU relit à son
/// démarrage pour se considérer connecté (docs/research/vslauncher-et-distribution.md, section d,
/// et <c>gameHandlers.ts</c> lignes 73-114 du dépôt de VS Launcher).
/// </summary>
/// <remarks>
/// Lecture-modification-écriture, jamais un simple remplacement : ce fichier appartient au jeu, il
/// contient toutes ses préférences (résolution, volumes, raccourcis, langue). Seules les huit clés
/// du contrat sont posées, tout le reste du document ressort à l'identique, et l'écriture passe par
/// <see cref="JsonFileStore"/> — donc temporaire puis déplacement — pour qu'une coupure ne laisse
/// jamais un joueur avec un fichier de réglages à moitié écrit.
/// </remarks>
public sealed class ClientSettingsSessionWriter
{
    /// <summary>Nom du fichier de réglages du jeu, dans le dossier de données de l'instance.</summary>
    public const string FileName = "clientsettings.json";

    private const string StringSettingsSection = "stringSettings";

    private readonly IFileSystem _fileSystem;
    private readonly JsonFileStore _jsonFileStore;

    public ClientSettingsSessionWriter(IFileSystem fileSystem, JsonFileStore jsonFileStore)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(jsonFileStore);

        _fileSystem = fileSystem;
        _jsonFileStore = jsonFileStore;
    }

    /// <summary>Chemin du <c>clientsettings.json</c> d'un dossier de données, qu'il existe ou non.</summary>
    public string GetSettingsFilePath(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        return _fileSystem.Path.Combine(dataDirectory, FileName);
    }

    /// <summary>
    /// Crée le fichier s'il n'existe pas, le met à jour sur place sinon.
    /// </summary>
    /// <exception cref="CorruptedFileException">
    /// Le fichier existe mais n'est pas du JSON exploitable. Rien n'est écrit : détruire les
    /// réglages de jeu de quelqu'un pour y poser une session serait un très mauvais échange.
    /// </exception>
    public async Task WriteAsync(string dataDirectory, VsSession session, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(session);

        var path = GetSettingsFilePath(dataDirectory);
        var document = await _jsonFileStore
            .ReadAsync(path, VsAuthJsonContext.Default.JsonObject, cancellationToken)
            .ConfigureAwait(false)
            ?? [];

        // Section absente, ou présente mais d'une forme que le jeu ne saurait pas relire non plus :
        // dans les deux cas elle est (re)construite, et elle seule.
        if (document[StringSettingsSection] is not JsonObject stringSettings)
        {
            stringSettings = [];
            document[StringSettingsSection] = stringSettings;
        }

        // L'orthographe de ces clés est le contrat, au caractère près. « hostgameserver » côté jeu
        // ne porte d'ailleurs pas le même nom que le « hasgameserver » de la réponse du service.
        stringSettings["mptoken"] = session.MpToken;
        stringSettings["sessionkey"] = session.SessionKey;
        stringSettings["sessionsignature"] = session.SessionSignature;
        stringSettings["useremail"] = session.Email;
        stringSettings["entitlements"] = session.Entitlements;
        stringSettings["playeruid"] = session.PlayerUid;
        stringSettings["playername"] = session.PlayerName;
        stringSettings["hostgameserver"] = session.HostGameServer;

        await _jsonFileStore
            .WriteAsync(path, document, VsAuthJsonContext.Default.JsonObject, cancellationToken)
            .ConfigureAwait(false);
    }
}
