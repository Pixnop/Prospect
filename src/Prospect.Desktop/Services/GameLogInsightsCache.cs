using Prospect.Core.Diagnostics;

namespace Prospect.Desktop.Services;

/// <inheritdoc cref="IGameLogInsightsCache" />
public sealed class GameLogInsightsCache : IGameLogInsightsCache
{
    private readonly Dictionary<string, InstanceLogInsights> _insights = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public InstanceLogInsights? TryGet(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        lock (_gate)
        {
            return _insights.GetValueOrDefault(slug);
        }
    }

    /// <inheritdoc />
    public void Store(string slug, InstanceLogInsights insights)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentNullException.ThrowIfNull(insights);

        lock (_gate)
        {
            _insights[slug] = insights;
        }
    }

    /// <inheritdoc />
    public void Invalidate(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        lock (_gate)
        {
            _insights.Remove(slug);
        }
    }
}