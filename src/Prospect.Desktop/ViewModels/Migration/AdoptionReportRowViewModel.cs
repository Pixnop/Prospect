namespace Prospect.Desktop.ViewModels.Migration;

/// <summary>Une ligne du rapport final d'adoption, qu'elle décrive une installation ou un moteur.</summary>
public sealed class AdoptionReportRowViewModel
{
    public AdoptionReportRowViewModel(string label, string? detail)
    {
        ArgumentException.ThrowIfNullOrEmpty(label);

        Label = label;
        DetailText = detail ?? string.Empty;
    }

    public string Label { get; }

    /// <summary>Raison de l'ignorance ou de l'échec ; vide pour une ligne adoptée.</summary>
    public string DetailText { get; }
}

/// <summary>Une catégorie du rapport final (adoptées, ignorées, en échec), avec ses lignes.</summary>
public sealed class AdoptionReportGroupViewModel
{
    public AdoptionReportGroupViewModel(string title, IReadOnlyList<AdoptionReportRowViewModel> rows)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentNullException.ThrowIfNull(rows);

        Title = title;
        Rows = rows;
    }

    public string Title { get; }

    public IReadOnlyList<AdoptionReportRowViewModel> Rows { get; }
}