using Prospect.Core.ModDb;

namespace Prospect.Desktop.Services;

/// <inheritdoc cref="IModUpdateCheckCache" />
public sealed class ModUpdateCheckCache : IModUpdateCheckCache
{
    private readonly Dictionary<string, InstanceUpdateReport> _reports = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public event EventHandler<ModUpdateCacheChange>? Changed;

    /// <inheritdoc />
    public InstanceUpdateReport? TryGet(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        lock (_gate)
        {
            return _reports.GetValueOrDefault(slug);
        }
    }

    /// <inheritdoc />
    public void Store(string slug, InstanceUpdateReport report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentNullException.ThrowIfNull(report);

        lock (_gate)
        {
            _reports[slug] = report;
        }

        Changed?.Invoke(this, new ModUpdateCacheChange(slug, report));
    }

    /// <inheritdoc />
    public void Invalidate(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        lock (_gate)
        {
            _reports.Remove(slug);
        }

        // Notifié inconditionnellement, même si rien n'était en cache : un appelant ne sait pas
        // toujours si une vérification a déjà eu lieu, et l'abonné (la carte d'Accueil) doit dans
        // les deux cas repartir à zéro plutôt que de garder un décompte potentiellement périmé.
        Changed?.Invoke(this, new ModUpdateCacheChange(slug, null));
    }
}