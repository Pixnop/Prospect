using System.IO.Abstractions.TestingHelpers;

using Prospect.Core.Auth;
using Prospect.Core.Storage;
using Prospect.Core.Tests.GameVersions;
using Prospect.Core.Tests.Storage;

using Shouldly;

namespace Prospect.Core.Tests.Auth;

/// <summary>
/// Le stockage de la session : un fichier à part de la configuration applicative, en permissions
/// restrictives, que la déconnexion supprime. C'est le point sur lequel Prospect diverge
/// volontairement de VS Launcher, qui écrivait tout ce secret en clair au milieu de son
/// <c>config.json</c> (docs/architecture.md, section « Après le MVP »).
/// </summary>
public sealed class FileSecretStoreTests
{
    private static readonly AppPaths Paths = new(new FakeAppEnvironment(), "/data/prospect");

    private static readonly VsSession Session = new()
    {
        Email = "joueuse@example.invalid",
        PlayerName = "Sylve",
        PlayerUid = "3f2b8e14",
        Entitlements = "singleplayer,multiplayer",
        SessionKey = "cle-de-session",
        SessionSignature = "signature-de-session",
        MpToken = "jeton-multijoueur",
        HostGameServer = "true",
    };

    private sealed record Fixture(FileSecretStore Store, MockFileSystem FileSystem, RecordingUnixFilePermissions Permissions);

    private static Fixture CreateFixture()
    {
        var fileSystem = new MockFileSystem();
        var permissions = new RecordingUnixFilePermissions();
        var store = new FileSecretStore(fileSystem, Paths, new JsonFileStore(fileSystem), permissions);

        return new Fixture(store, fileSystem, permissions);
    }

    [Fact]
    public void Constructor_NullArguments_ThrowArgumentNullException()
    {
        var fileSystem = new MockFileSystem();

        Should.Throw<ArgumentNullException>(() => new FileSecretStore(null!, Paths, new JsonFileStore(fileSystem), new RecordingUnixFilePermissions()));
        Should.Throw<ArgumentNullException>(() => new FileSecretStore(fileSystem, null!, new JsonFileStore(fileSystem), new RecordingUnixFilePermissions()));
        Should.Throw<ArgumentNullException>(() => new FileSecretStore(fileSystem, Paths, null!, new RecordingUnixFilePermissions()));
        Should.Throw<ArgumentNullException>(() => new FileSecretStore(fileSystem, Paths, new JsonFileStore(fileSystem), null!));
    }

    [Fact]
    public async Task SaveAsync_WritesSessionJsonAtTheProspectRootBesideButNotInsideTheSettings()
    {
        var fixture = CreateFixture();

        await fixture.Store.SaveAsync(Session, CancellationToken.None);

        fixture.FileSystem.File.Exists(Paths.SessionFilePath).ShouldBeTrue();
        Paths.SessionFilePath.ShouldNotBe(Paths.SettingsFilePath);
        var settingsContent = fixture.FileSystem.File.Exists(Paths.SettingsFilePath)
            ? fixture.FileSystem.File.ReadAllText(Paths.SettingsFilePath)
            : string.Empty;
        settingsContent.ShouldNotContain("cle-de-session");
    }

    [Fact]
    public async Task SaveAsync_AppliesOwnerOnlyPermissionsToTheStoredFile()
    {
        var fixture = CreateFixture();

        await fixture.Store.SaveAsync(Session, CancellationToken.None);

        fixture.Permissions.Modes[Paths.SessionFilePath]
            .ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public async Task SaveAsync_AppliesOwnerOnlyPermissionsToTheTemporaryFileBeforeItHoldsAnySecret()
    {
        // L'écriture atomique passe par « <cible>.tmp » puis un déplacement : le fichier temporaire
        // porte donc le secret complet avant d'exister sous son nom final. Le laisser naître avec
        // les permissions par défaut du umask rendrait la restriction posée après coup purement
        // décorative.
        var fixture = CreateFixture();

        await fixture.Store.SaveAsync(Session, CancellationToken.None);

        fixture.Permissions.Modes[Paths.SessionFilePath + JsonFileStore.TempFileSuffix]
            .ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsEveryFieldOfTheSession()
    {
        var fixture = CreateFixture();

        await fixture.Store.SaveAsync(Session, CancellationToken.None);
        var loaded = await fixture.Store.LoadAsync(CancellationToken.None);

        loaded.ShouldNotBeNull();
        loaded.ShouldBe(Session);
    }

    [Fact]
    public async Task SaveAsync_StoredDocumentHoldsNoCredentialBeyondTheSessionItself()
    {
        var fixture = CreateFixture();

        await fixture.Store.SaveAsync(Session, CancellationToken.None);

        var content = fixture.FileSystem.File.ReadAllText(Paths.SessionFilePath);
        content.ShouldNotContain("password", Case.Insensitive);
        content.ShouldNotContain("motDePasse", Case.Insensitive);
    }

    [Fact]
    public async Task SaveAsync_Twice_ReplacesThePreviousSessionRatherThanAppending()
    {
        var fixture = CreateFixture();
        await fixture.Store.SaveAsync(Session, CancellationToken.None);

        await fixture.Store.SaveAsync(Session with { PlayerName = "Aubier", SessionKey = "nouvelle-cle" }, CancellationToken.None);

        var loaded = await fixture.Store.LoadAsync(CancellationToken.None);
        loaded.ShouldNotBeNull().PlayerName.ShouldBe("Aubier");
        var content = fixture.FileSystem.File.ReadAllText(Paths.SessionFilePath);
        content.ShouldNotContain("cle-de-session");
    }

    [Fact]
    public async Task LoadAsync_NoFileYet_ReturnsNullBecauseNotSignedInIsANormalState()
    {
        var fixture = CreateFixture();

        (await fixture.Store.LoadAsync(CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task LoadAsync_CorruptedFile_IsToleratedAsSignedOutRatherThanCrashingTheStartup()
    {
        var fixture = CreateFixture();
        fixture.FileSystem.AddFile(Paths.SessionFilePath, new MockFileData("{ ceci n'est pas du JSON"));

        (await fixture.Store.LoadAsync(CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task LoadAsync_TruncatedDocumentMissingFields_IsToleratedAsSignedOut()
    {
        var fixture = CreateFixture();
        fixture.FileSystem.AddFile(Paths.SessionFilePath, new MockFileData("""{ "playerName": "Sylve" }"""));

        (await fixture.Store.LoadAsync(CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task LoadAsync_DocumentWithoutSessionMaterial_IsToleratedAsSignedOut()
    {
        var fixture = CreateFixture();
        await fixture.Store.SaveAsync(Session with { SessionKey = string.Empty }, CancellationToken.None);

        (await fixture.Store.LoadAsync(CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task ClearAsync_DeletesTheStoredSessionFile()
    {
        var fixture = CreateFixture();
        await fixture.Store.SaveAsync(Session, CancellationToken.None);

        await fixture.Store.ClearAsync(CancellationToken.None);

        fixture.FileSystem.File.Exists(Paths.SessionFilePath).ShouldBeFalse();
        (await fixture.Store.LoadAsync(CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task ClearAsync_NothingStored_IsANoOp()
    {
        var fixture = CreateFixture();

        await Should.NotThrowAsync(() => fixture.Store.ClearAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_NullSession_ThrowsWithoutWritingAnything()
    {
        var fixture = CreateFixture();

        await Should.ThrowAsync<ArgumentNullException>(() => fixture.Store.SaveAsync(null!, CancellationToken.None));

        fixture.FileSystem.File.Exists(Paths.SessionFilePath).ShouldBeFalse();
    }
}
