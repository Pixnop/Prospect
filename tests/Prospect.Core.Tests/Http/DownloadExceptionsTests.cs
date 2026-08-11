using Prospect.Core.GameVersions;
using Prospect.Core.Http;

using Shouldly;

namespace Prospect.Core.Tests.Http;

public sealed class DownloadExceptionsTests
{
    [Fact]
    public void DownloadFailedException_DefaultConstructors_CarryAUsableMessage()
    {
        new DownloadFailedException().Message.ShouldNotBeNullOrWhiteSpace();
        new DownloadFailedException("détail").Message.ShouldBe("détail");
        new DownloadFailedException("détail", new IOException()).InnerException.ShouldBeOfType<IOException>();
    }

    [Fact]
    public void ForFile_NamesTheFileAndKeepsTheOriginalFailure()
    {
        var exception = DownloadFailedException.ForFile("client.tar.gz", new IOException("coupure"));

        exception.Message.ShouldContain("client.tar.gz");
        exception.InnerException.ShouldBeOfType<IOException>();
    }

    [Fact]
    public void ChecksumMismatch_DefaultConstructors_CarryAUsableMessage()
    {
        new DownloadChecksumMismatchException().Message.ShouldNotBeNullOrWhiteSpace();
        new DownloadChecksumMismatchException("détail").Message.ShouldBe("détail");
        new DownloadChecksumMismatchException("détail", new IOException()).InnerException.ShouldBeOfType<IOException>();
    }

    [Fact]
    public void ChecksumMismatch_Create_ExposesBothDigests()
    {
        var exception = DownloadChecksumMismatchException.Create("client.tar.gz", "aaaa", "bbbb");

        exception.FileName.ShouldBe("client.tar.gz");
        exception.ExpectedMd5.ShouldBe("aaaa");
        exception.ActualMd5.ShouldBe("bbbb");
        exception.ShouldBeAssignableTo<DownloadFailedException>();
    }

    [Fact]
    public void CatalogUnavailable_Constructors_CarryAUsableMessage()
    {
        new GameCatalogUnavailableException().Message.ShouldNotBeNullOrWhiteSpace();
        new GameCatalogUnavailableException("détail").Message.ShouldBe("détail");
        new GameCatalogUnavailableException("détail", new IOException()).InnerException.ShouldBeOfType<IOException>();
    }
}