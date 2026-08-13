using Avalonia.Media.Imaging;

using Prospect.Desktop.Services;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>
/// Faux <see cref="IModLogoCache"/> pour les tests de <see cref="Prospect.Desktop.ViewModels.Mods.ModCardViewModel"/> :
/// pas de réseau, juste un moyen d'observer ce que la carte demande (nombre d'appels, jeton
/// d'annulation transmis) et de contrôler quand l'appel se termine, sans dépendre d'un
/// <see cref="HttpMessageHandler"/> ni du décodage réel d'une image.
/// </summary>
internal sealed class FakeModLogoCache : IModLogoCache
{
    private readonly Bitmap? _result;
    private readonly bool _hangUntilCanceled;

    /// <param name="result">Ce que rend l'appel une fois terminé.</param>
    /// <param name="hangUntilCanceled">
    /// Vrai pour ne jamais se terminer tant que le jeton d'annulation ne l'impose pas : c'est ce
    /// qui rend le scénario d'annulation déterministe (voir <see cref="LastCancellationToken"/>).
    /// </param>
    public FakeModLogoCache(Bitmap? result = null, bool hangUntilCanceled = false)
    {
        _result = result;
        _hangUntilCanceled = hangUntilCanceled;
    }

    /// <summary>Nombre de fois où <see cref="GetAsync"/> a été appelé.</summary>
    public int CallCount { get; private set; }

    /// <summary>URL reçue au dernier appel.</summary>
    public Uri? LastUrl { get; private set; }

    /// <summary>Jeton reçu au dernier appel, pour vérifier qu'un <c>Dispose</c> de la carte l'annule bien.</summary>
    public CancellationToken LastCancellationToken { get; private set; }

    /// <summary>Largeur d'usage reçue au dernier appel : c'est elle qui décide du coût mémoire réel.</summary>
    public int LastMaxWidth { get; private set; }

    public Task<Bitmap?> GetAsync(Uri logoUrl, CancellationToken cancellationToken = default)
        => GetAsync(logoUrl, Prospect.Desktop.Services.ModLogoCache.MaxLogoWidth, cancellationToken);

    public async Task<Bitmap?> GetAsync(Uri imageUrl, int maxWidth, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastUrl = imageUrl;
        LastMaxWidth = maxWidth;
        LastCancellationToken = cancellationToken;

        if (_hangUntilCanceled)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }

        return _result;
    }
}