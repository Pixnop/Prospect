using Prospect.Core.Common;

namespace Prospect.Core.Tests.Common;

/// <summary>
/// Double de test d'<see cref="IAppLog"/> : garde en mémoire ce qui a été journalisé, pour que les
/// tests vérifient la PIÈCE qu'un rapport de terrain pourra citer (la ligne de commande de
/// l'installeur, le verdict de la vérification post-installation).
/// </summary>
internal sealed class RecordingAppLog : IAppLog
{
    public List<(AppLogLevel Level, string Message)> Lines { get; } = [];

    public void Write(AppLogLevel level, string message) => Lines.Add((level, message));
}