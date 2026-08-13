using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;

using Prospect.Core.Common;
using Prospect.Core.Diagnostics;
using Prospect.Core.Settings;
using Prospect.Core.Storage;
using Prospect.Desktop.Resources;
using Prospect.Desktop.Tests.TestDoubles;
using Prospect.Desktop.ViewModels.Logs;
using Prospect.Desktop.ViewModels.Toasts;

using Shouldly;

namespace Prospect.Desktop.Tests.ViewModels.Logs;

/// <summary>
/// La page Journaux : ce qu'elle montre, ce qu'elle emporte, et ce qu'elle dit quand il n'y a rien.
/// </summary>
public sealed class LogsViewModelTests
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");
    private static readonly DateTimeOffset Noon = new(2026, 8, 13, 12, 30, 15, TimeSpan.Zero);

    private sealed record Harness(
        LogsViewModel ViewModel,
        MockFileSystem FileSystem,
        FakeFilePickerService FilePicker,
        RecordingToastService Toasts,
        FileAppLog Log);

    private static Harness Create()
    {
        var fileSystem = new MockFileSystem();
        var picker = new FakeFilePickerService();
        var toasts = new RecordingToastService();
        var service = new AppLogService(fileSystem, Paths);

        return new Harness(
            new LogsViewModel(service, picker, toasts),
            fileSystem,
            picker,
            toasts,
            new FileAppLog(fileSystem, Paths, new FakeClock(Noon)));
    }

    [Fact]
    public void WithoutAnyLogYet_ThePageIsHonestlyEmpty()
    {
        var harness = Create();

        harness.ViewModel.RefreshCommand.Execute(null);

        harness.ViewModel.Lines.ShouldBeEmpty();
        harness.ViewModel.HasLines.ShouldBeFalse();
        harness.ViewModel.IsEmpty.ShouldBeTrue();
        harness.ViewModel.ExportableFileCount.ShouldBe(0);
        harness.ViewModel.CanExport.ShouldBeFalse();
        harness.ViewModel.SubtitleText.ShouldBe("aucune ligne · aucun journal à exporter");
    }

    [Fact]
    public void Refresh_ShowsTheLinesWithTheirTone()
    {
        var harness = Create();
        harness.Log.Write(AppLogLevel.Info, "Version 1.22.6 installée.");
        harness.Log.Write(AppLogLevel.Warning, "Miroir cdn injoignable.");
        harness.Log.Write(AppLogLevel.Error, "Aucun exécutable attendu.");

        harness.ViewModel.RefreshCommand.Execute(null);

        harness.ViewModel.Lines.Select(line => line.Tone).ShouldBe(["plain", "warn", "error"]);
        harness.ViewModel.Lines[^1].Text.ShouldContain("Aucun exécutable attendu.");
        harness.ViewModel.HasLines.ShouldBeTrue();
        harness.ViewModel.IsEmpty.ShouldBeFalse();
        harness.ViewModel.SubtitleText.ShouldBe("3 dernières lignes · 1 journal à exporter");
    }

    /// <summary>
    /// La page nomme le fichier qu'elle lit : sans ça, personne ne sait où aller le chercher quand
    /// il faut le joindre à la main.
    /// </summary>
    [Fact]
    public void ThePage_NamesTheFileItReads()
        => Create().ViewModel.LogPathText.ShouldEndWith("prospect.log");

    [Fact]
    public void Refresh_CountsTheInstanceLogsAsExportable()
    {
        var harness = Create();
        harness.Log.Write(AppLogLevel.Info, "démarrage");
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(Paths.LogsDirectory, "instance-survie.log"),
            new MockFileData("sortie du jeu"));

        harness.ViewModel.RefreshCommand.Execute(null);

        harness.ViewModel.ExportableFileCount.ShouldBe(2);
        harness.ViewModel.CanExport.ShouldBeTrue();
        harness.ViewModel.SubtitleText.ShouldEndWith("2 journaux à exporter");
    }

    [Fact]
    public async Task Export_WritesTheZipWhereThePickerSaysAndTellsTheUser()
    {
        var harness = Create();
        harness.Log.Write(AppLogLevel.Info, "démarrage");
        harness.FileSystem.AddFile(
            harness.FileSystem.Path.Combine(Paths.LogsDirectory, "instance-survie.log"),
            new MockFileData("sortie du jeu"));
        harness.ViewModel.RefreshCommand.Execute(null);
        harness.FilePicker.NextSavePath = "/home/jean/prospect-journaux.zip";

        await harness.ViewModel.ExportCommand.ExecuteAsync(null);

        var request = harness.FilePicker.SaveRequests.ShouldHaveSingleItem();
        request.Extension.ShouldBe("zip");
        request.SuggestedFileName.ShouldBe("prospect-journaux.zip");

        using var archive = new ZipArchive(harness.FileSystem.File.OpenRead("/home/jean/prospect-journaux.zip"), ZipArchiveMode.Read);
        archive.Entries.Select(entry => entry.FullName).ShouldBe(["prospect.log", "instance-survie.log"]);

        var toast = harness.Toasts.Shown.ShouldHaveSingleItem();
        toast.Tone.ShouldBe(ToastTone.Success);
        toast.Description.ShouldBe("2 journaux dans l'archive.");
    }

    /// <summary>Sélecteur annulé : rien n'est écrit, et ce n'est pas une erreur.</summary>
    [Fact]
    public async Task Export_CanceledPicker_WritesNothingAndSaysNothing()
    {
        var harness = Create();
        harness.Log.Write(AppLogLevel.Info, "démarrage");
        harness.ViewModel.RefreshCommand.Execute(null);
        harness.FilePicker.NextSavePath = null;

        await harness.ViewModel.ExportCommand.ExecuteAsync(null);

        harness.Toasts.Shown.ShouldBeEmpty();
        harness.ViewModel.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public void ExportedToastDescription_ReadsInEnglishToo()
    {
        var english = UiText.TableFor(ProspectSettings.English).Logs;

        english.ExportedToastDescription(1).ShouldBe("1 log in the archive.");
        english.ExportedToastDescription(3).ShouldBe("3 logs in the archive.");
        english.Subtitle(0, 0).ShouldBe("no lines · no log to export");
        english.Subtitle(12, 2).ShouldBe("last 12 lines · 2 logs to export");
        english.ExportFileName.ShouldBe("prospect-logs.zip");
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var service = new AppLogService(new MockFileSystem(), Paths);

        Should.Throw<ArgumentNullException>(() => new LogsViewModel(null!, new FakeFilePickerService(), new RecordingToastService()));
        Should.Throw<ArgumentNullException>(() => new LogsViewModel(service, null!, new RecordingToastService()));
        Should.Throw<ArgumentNullException>(() => new LogsViewModel(service, new FakeFilePickerService(), null!));
    }
}