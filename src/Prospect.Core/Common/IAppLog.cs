using System.Globalization;
using System.IO.Abstractions;

using Prospect.Core.Storage;

namespace Prospect.Core.Common;

/// <summary>Gravité d'une ligne du journal de diagnostic.</summary>
public enum AppLogLevel
{
    /// <summary>Fait notable du déroulement normal (ce qui a été lancé, ce qui s'est terminé).</summary>
    Info,

    /// <summary>Anomalie rattrapée : l'opération continue mais quelque chose mérite d'être su.</summary>
    Warning,

    /// <summary>Échec.</summary>
    Error,
}

/// <summary>
/// Port vers le journal de diagnostic de l'application (<c>logs/prospect.log</c>), distinct du
/// journal de lancement d'une instance que <c>GameLauncher</c> écrit déjà par instance.
/// </summary>
/// <remarks>
/// <para>
/// Il existe pour une raison précise et étroite : un défaut rapporté depuis une machine
/// d'utilisateur ne se diagnostique que sur pièce. Le cas fondateur est l'installeur Windows, dont
/// on ne peut pas savoir, depuis un rapport, si les arguments de silence sont bien arrivés ni où
/// l'installation a réellement atterri. Ce n'est donc pas une trace de développement à semer
/// partout : on y écrit ce qu'un rapport de terrain devrait pouvoir citer.
/// </para>
/// <para>
/// CE QUI S'Y ÉCRIT, et la règle vaut pour toute évolution : les FAITS d'une session que
/// l'utilisateur pourrait décrire au téléphone. Le catalogue relu, un téléchargement commencé,
/// fini ou échoué avec son nom et sa taille, une version installée ou retirée, une instance créée,
/// dupliquée ou supprimée, un lancement avec son pid et la sortie du jeu avec son code, un mod
/// posé, remplacé, activé ou retiré, une vérification de mises à jour avec son verdict compté, et
/// toute erreur qu'on a montrée à l'utilisateur. Jamais un secret (mot de passe, jeton, session),
/// jamais une ligne par image ni par octet : ce qui se répète à la fréquence d'une boucle n'est
/// pas un fait, c'est du bruit qui écrase le fichier (voir le plafond de
/// <see cref="FileAppLog.MaxSizeBytes"/>). Un avancement de téléchargement se résume à ses deux
/// bouts, pas à ses mille rapports intermédiaires.
/// </para>
/// <para>
/// COMMENT IL S'INJECTE. C'est un port TRANSVERSE, présent dans presque tous les services du
/// domaine, et il est le seul du projet à être un paramètre de constructeur OPTIONNEL, en dernière
/// position, avec <see cref="NullAppLog.Instance"/> pour valeur de repli. Le conteneur le résout
/// comme les autres et la composition root en passe donc toujours un vrai ; l'optionnalité ne sert
/// qu'aux tests, qui construisent ces services par dizaines et pour qui un journal n'est presque
/// jamais le sujet. L'imposer partout aurait ajouté un argument mécanique à quatre-vingts appels
/// sans rendre un seul test plus vrai. La contrepartie est nommée pour qu'elle ne se perde pas :
/// un service dont le journal manquerait en production n'écrirait rien sans se plaindre, et c'est
/// pourquoi <c>CompositionRootTests</c> vérifie que le conteneur livre bien un
/// <see cref="FileAppLog"/> à ces services-là.
/// </para>
/// </remarks>
public interface IAppLog
{
    /// <summary>Écrit une ligne. Ne lève jamais : un journal en échec ne doit pas faire échouer l'opération journalisée.</summary>
    void Write(AppLogLevel level, string message);
}

/// <summary>
/// Implémentation d'<see cref="IAppLog"/> qui n'écrit rien. Valeur par défaut des tests et des
/// scénarios où aucun disque n'est disponible.
/// </summary>
public sealed class NullAppLog : IAppLog
{
    /// <summary>Instance partagée : la classe est sans état.</summary>
    public static NullAppLog Instance { get; } = new();

    /// <inheritdoc />
    public void Write(AppLogLevel level, string message)
    {
        // Volontairement vide : c'est le contrat de ce double.
    }
}

/// <summary>
/// Implémentation d'<see cref="IAppLog"/> qui ajoute une ligne horodatée à
/// <c>logs/prospect.log</c>, via le système de fichiers abstrait.
/// </summary>
/// <remarks>
/// Trois choix, tous dictés par l'usage visé (un fichier qu'un utilisateur joint à un rapport) :
/// horodatage UTC en ISO 8601 pour que deux rapports se recoupent sans discuter de fuseau, écriture
/// sérialisée par un verrou parce que téléchargements et installation écrivent depuis plusieurs
/// tâches, et une taille PLAFONNÉE au-delà de laquelle le fichier repart de zéro. Le plafond vaut
/// mieux qu'une rotation multi-fichiers ici : personne ne va lire l'avant-dernier journal d'un
/// launcher, et un fichier qui grossit sans fin finit par être le vrai bug.
/// </remarks>
public sealed class FileAppLog : IAppLog
{
    /// <summary>Nom du fichier, sous <c>logs/</c>.</summary>
    public const string FileName = "prospect.log";

    /// <summary>Taille au-delà de laquelle le journal repart de zéro, en octets.</summary>
    public const long MaxSizeBytes = 1024 * 1024;

    private readonly IFileSystem _fileSystem;
    private readonly AppPaths _appPaths;
    private readonly IClock _clock;
    private readonly Lock _gate = new();

    /// <summary>Construit le journal.</summary>
    /// <param name="fileSystem">Système de fichiers abstrait.</param>
    /// <param name="appPaths">Arborescence de l'application, pour <c>logs/</c>.</param>
    /// <param name="clock">Horloge, pour l'horodatage.</param>
    public FileAppLog(IFileSystem fileSystem, AppPaths appPaths, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(appPaths);
        ArgumentNullException.ThrowIfNull(clock);

        _fileSystem = fileSystem;
        _appPaths = appPaths;
        _clock = clock;
    }

    /// <summary>Chemin du fichier journal.</summary>
    public string FilePath => _fileSystem.Path.Combine(_appPaths.LogsDirectory, FileName);

    /// <inheritdoc />
    public void Write(AppLogLevel level, string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{_clock.UtcNow:yyyy-MM-dd'T'HH:mm:ss'Z'} [{Label(level)}] {message}{Environment.NewLine}");

        // Bloc large exprès : un journal indisponible (disque plein, dossier en lecture seule,
        // fichier verrouillé) ne doit jamais faire échouer l'opération qu'il décrit.
        try
        {
            lock (_gate)
            {
                _fileSystem.Directory.CreateDirectory(_appPaths.LogsDirectory);
                var path = FilePath;

                if (_fileSystem.File.Exists(path) && _fileSystem.FileInfo.New(path).Length > MaxSizeBytes)
                {
                    _fileSystem.File.Delete(path);
                }

                _fileSystem.File.AppendAllText(path, line);
            }
        }
        catch (Exception)
        {
            // Rien à faire : perdre une ligne de journal est sans conséquence.
        }
    }

    private static string Label(AppLogLevel level) => level switch
    {
        AppLogLevel.Warning => "WARN",
        AppLogLevel.Error => "ERROR",
        _ => "INFO",
    };
}