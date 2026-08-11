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

    /// <summary>Chemin rendu par le prochain appel à <see cref="PickOpenFileAsync"/>, ou <see langword="null"/> pour simuler une annulation.</summary>
    public string? NextOpenPath { get; set; }

    public List<(string Title, string SuggestedFileName, string Extension)> SaveRequests { get; } = [];

    public List<(string Title, IReadOnlyList<string> Extensions)> OpenRequests { get; } = [];

    public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, CancellationToken cancellationToken = default)
    {
        SaveRequests.Add((title, suggestedFileName, extension));

        return Task.FromResult(NextSavePath);
    }

    public Task<string?> PickOpenFileAsync(string title, IReadOnlyList<string> extensions, CancellationToken cancellationToken = default)
    {
        OpenRequests.Add((title, extensions));

        return Task.FromResult(NextOpenPath);
    }
}