using Prospect.Core.Instances;
using Prospect.Desktop.Formatting;

namespace Prospect.Desktop.ViewModels.Instance;

/// <summary>Une ligne de l'onglet Mondes : un fichier de <c>data/Saves/</c>, tel que rendu par <see cref="Prospect.Core.Instances.IInstanceRepository.ListWorldsAsync"/>.</summary>
public sealed class InstanceWorldRowViewModel
{
    public InstanceWorldRowViewModel(InstanceWorldFile file, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(file);

        Name = file.FileName;
        SizeText = ByteSizeFormatter.Format(file.SizeInBytes);
        LastModifiedText = RelativeDateFormatter.Format(file.LastModifiedUtc, now);
    }

    public string Name { get; }

    public string SizeText { get; }

    public string LastModifiedText { get; }
}