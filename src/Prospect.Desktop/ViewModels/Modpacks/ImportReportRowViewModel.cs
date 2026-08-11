using Prospect.Core.Modpacks;
using Prospect.Desktop.Resources;

namespace Prospect.Desktop.ViewModels.Modpacks;

/// <summary>Une ligne du rapport final : un mod du manifest et ce qui lui est arrivé.</summary>
public sealed class ImportReportRowViewModel
{
    /// <summary>Construit la ligne.</summary>
    public ImportReportRowViewModel(ModpackImportModReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        ModId = report.ModId;
        DetailText = UiText.Modpacks.ReportRowDetail(report);
    }

    public string ModId { get; }

    /// <summary>Complément court : version installée, suggestion, ou détail technique bref.</summary>
    public string DetailText { get; }
}

/// <summary>Une catégorie du rapport final (installés, introuvables, ...), avec ses lignes.</summary>
public sealed class ImportReportGroupViewModel
{
    /// <summary>Construit le groupe.</summary>
    public ImportReportGroupViewModel(string title, IReadOnlyList<ImportReportRowViewModel> rows)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentNullException.ThrowIfNull(rows);

        Title = title;
        Rows = rows;
    }

    public string Title { get; }

    public IReadOnlyList<ImportReportRowViewModel> Rows { get; }
}