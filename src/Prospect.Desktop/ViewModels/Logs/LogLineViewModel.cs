using Prospect.Core.Common;
using Prospect.Core.Diagnostics;

namespace Prospect.Desktop.ViewModels.Logs;

/// <summary>
/// Une ligne de la page Journaux : le texte tel qu'il est sur disque, et le TON que son niveau lui
/// vaut.
/// </summary>
/// <remarks>
/// Le ton est un mot, pas un pinceau : le ViewModel ne connaît aucun type Avalonia, la vue en fait
/// une classe de style et le thème clair suit sans que rien change ici. Même mécanique que les tons
/// de badge de l'écran Versions (<c>ChannelBadgePresentation</c>).
/// </remarks>
public sealed class LogLineViewModel
{
    /// <summary>Construit la ligne depuis l'entrée relue du journal.</summary>
    public LogLineViewModel(AppLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Text = entry.Text;
        Tone = entry.Level switch
        {
            AppLogLevel.Error => "error",
            AppLogLevel.Warning => "warn",

            // Info comme les lignes sans entête : le ton ordinaire. Teindre l'ordinaire ne
            // distinguerait plus rien, et c'est l'inhabituel qu'on cherche en ouvrant un journal.
            _ => "plain",
        };
    }

    /// <summary>Ligne complète, horodatage et étiquette compris.</summary>
    public string Text { get; }

    /// <summary>Ton d'affichage : <c>error</c>, <c>warn</c> ou <c>plain</c>.</summary>
    public string Tone { get; }
}