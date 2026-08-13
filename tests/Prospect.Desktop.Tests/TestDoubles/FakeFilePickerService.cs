using Prospect.Desktop.Services;

namespace Prospect.Desktop.Tests.TestDoubles;

/// <summary>
/// Double de test d'<see cref="IFilePickerService"/> : rend un chemin scripté plutôt que d'ouvrir
/// un vrai sélecteur, et journalise chaque appel pour les tests qui veulent vérifier ce qui a été
/// proposé (nom suggéré, extensions).
/// </summary>
internal sealed class FakeFilePickerService : IFilePickerService
{
    /// <summary>Chemin rendu par le prochain appel à <see cref="PickSaveFileAsync"/>, ou <see langword="null"/> pour simuler une annulation.</summary>
    public string? NextSavePath { get; set; }

    /// <summary>Chemin rendu par le prochain appel à <see cref="PickFolderAsync"/>, ou <see langword="null"/> pour simuler une annulation.</summary>
    public string? NextFolderPath { get; set; }

    public List<(string Title, string SuggestedFileName, string Extension)> SaveRequests { get; } = [];

    public List<string> FolderRequests { get; } = [];

    public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, CancellationToken cancellationToken = default)
    {
        SaveRequests.Add((title, suggestedFileName, extension));

        return Task.FromResult(NextSavePath);
    }

    public Task<string?> PickFolderAsync(string title, CancellationToken cancellationToken = default)
    {
        FolderRequests.Add(title);

        return Task.FromResult(NextFolderPath);
    }
}