using System.Text;

using MediaOrganizer.Configuration;
using MediaOrganizer.Torrents;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace MediaOrganizer.Tests;

public class TorrentServiceTests
{
    private readonly Mock<ITorrentClient> _clientMock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] ValidTorrentBytes() =>
        Encoding.Latin1.GetBytes("d4:infod4:name4:testee");

    private TorrentService CreateService(
        string? url = "http://qbittorrent:8488",
        string? sourceFolder = "/media",
        string? downloadFolder = null)
    {
        var options = new MediaOrganizerOptions
        {
            SourceFolder = sourceFolder,
            Qbittorrent = new QbittorrentOptions
            {
                Url = url,
                Username = "admin",
                Password = "secret",
                DownloadFolder = downloadFolder
            }
        };

        return new TorrentService(NullLogger<TorrentService>.Instance, Options.Create(options), _clientMock.Object);
    }

    [Fact]
    public async Task AddTorrentAsync_UsesConfiguredDownloadFolder()
    {
        var sut = CreateService(downloadFolder: "/media/incoming");
        var content = ValidTorrentBytes();

        var result = await sut.AddTorrentAsync("movie.torrent", content, cancellationToken: Ct);

        Assert.Equal("movie.torrent", result.FileName);
        Assert.Equal("/media/incoming", result.SavePath);
        Assert.Equal(content.Length, result.SizeBytes);
        _clientMock.Verify(
            c => c.AddTorrentFileAsync("movie.torrent", content, "/media/incoming", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AddTorrentAsync_FallsBackToSourceFolderWhenDownloadFolderMissing()
    {
        var sut = CreateService(downloadFolder: null);

        var result = await sut.AddTorrentAsync("movie.torrent", ValidTorrentBytes(), cancellationToken: Ct);

        Assert.Equal("/media", result.SavePath);
    }

    [Fact]
    public async Task AddTorrentAsync_UsesFolderPathOverride()
    {
        var sut = CreateService(downloadFolder: "/media/incoming");

        var result = await sut.AddTorrentAsync("movie.torrent", ValidTorrentBytes(), "/media/other", Ct);

        Assert.Equal("/media/other", result.SavePath);
        _clientMock.Verify(
            c => c.AddTorrentFileAsync(It.IsAny<string>(), It.IsAny<byte[]>(), "/media/other", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AddTorrentAsync_TrimsFileNameToLeaf()
    {
        var sut = CreateService();

        var result = await sut.AddTorrentAsync("/tmp/uploads/movie.torrent", ValidTorrentBytes(), cancellationToken: Ct);

        Assert.Equal("movie.torrent", result.FileName);
    }

    [Theory]
    [InlineData("movie.txt")]
    [InlineData("movie")]
    [InlineData("movie.torrent.exe")]
    public async Task AddTorrentAsync_RejectsNonTorrentExtension(string fileName)
    {
        var sut = CreateService();

        await Assert.ThrowsAsync<TorrentValidationException>(
            () => sut.AddTorrentAsync(fileName, ValidTorrentBytes(), cancellationToken: Ct));
    }

    [Fact]
    public async Task AddTorrentAsync_RejectsEmptyContent()
    {
        var sut = CreateService();

        await Assert.ThrowsAsync<TorrentValidationException>(
            () => sut.AddTorrentAsync("movie.torrent", Array.Empty<byte>(), cancellationToken: Ct));
    }

    [Fact]
    public async Task AddTorrentAsync_RejectsNonTorrentContent()
    {
        var sut = CreateService();
        var content = Encoding.Latin1.GetBytes("this is not a torrent file");

        await Assert.ThrowsAsync<TorrentValidationException>(
            () => sut.AddTorrentAsync("movie.torrent", content, cancellationToken: Ct));

        _clientMock.Verify(
            c => c.AddTorrentFileAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AddTorrentAsync_RejectsMissingFileName()
    {
        var sut = CreateService();

        await Assert.ThrowsAsync<TorrentValidationException>(
            () => sut.AddTorrentAsync(null, ValidTorrentBytes(), cancellationToken: Ct));
    }

    [Fact]
    public async Task AddTorrentAsync_ThrowsWhenNotConfigured()
    {
        var sut = CreateService(url: null);

        await Assert.ThrowsAsync<QbittorrentNotConfiguredException>(
            () => sut.AddTorrentAsync("movie.torrent", ValidTorrentBytes(), cancellationToken: Ct));
    }

    [Fact]
    public async Task AddTorrentAsync_RejectsRelativeFolderPathOverride()
    {
        var sut = CreateService();

        await Assert.ThrowsAsync<TorrentValidationException>(
            () => sut.AddTorrentAsync("movie.torrent", ValidTorrentBytes(), "relative/path", Ct));
    }

    [Fact]
    public async Task AddTorrentAsync_RejectsParentDirectorySegments()
    {
        var sut = CreateService();

        await Assert.ThrowsAsync<TorrentValidationException>(
            () => sut.AddTorrentAsync("movie.torrent", ValidTorrentBytes(), "/media/../etc", Ct));
    }

    [Fact]
    public async Task AddTorrentAsync_ThrowsWhenNoDownloadFolderAvailable()
    {
        var sut = CreateService(sourceFolder: null, downloadFolder: null);

        await Assert.ThrowsAsync<TorrentValidationException>(
            () => sut.AddTorrentAsync("movie.torrent", ValidTorrentBytes(), cancellationToken: Ct));
    }

    [Fact]
    public async Task AddTorrentAsync_PropagatesClientFailure()
    {
        _clientMock
            .Setup(c => c.AddTorrentFileAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new QbittorrentException("qBittorrent rejected the torrent file as invalid."));

        var sut = CreateService();

        await Assert.ThrowsAsync<QbittorrentException>(
            () => sut.AddTorrentAsync("movie.torrent", ValidTorrentBytes(), cancellationToken: Ct));
    }

    [Fact]
    public void IsConfigured_ReflectsConfiguredUrl()
    {
        Assert.True(CreateService(url: "http://qbittorrent:8488").IsConfigured);
        Assert.False(CreateService(url: null).IsConfigured);
        Assert.False(CreateService(url: "  ").IsConfigured);
    }
[Fact]
    public async Task AddMagnetAsync_SendsLinkToClient()
    {
        var sut = CreateService(downloadFolder: "/media/incoming");
        const string magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Some.Movie.2024";

        var result = await sut.AddMagnetAsync(magnet, cancellationToken: Ct);

        Assert.Equal("/media/incoming", result.SavePath);
        _clientMock.Verify(
            c => c.AddTorrentUrlAsync(magnet, "/media/incoming", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AddMagnetAsync_UsesDnParameterAsDisplayName()
    {
        var sut = CreateService();
        const string magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Some.Movie.2024";

        var result = await sut.AddMagnetAsync(magnet, cancellationToken: Ct);

        Assert.Equal("Some.Movie.2024", result.FileName);
    }

    [Fact]
    public async Task AddMagnetAsync_FallsBackToInfoHashWhenNoDisplayName()
    {
        var sut = CreateService();
        const string magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";

        var result = await sut.AddMagnetAsync(magnet, cancellationToken: Ct);

        Assert.Equal("magnet:0123456789abcdef0123456789abcdef01234567", result.FileName);
    }

    [Fact]
    public async Task AddMagnetAsync_AcceptsHttpTorrentUrl()
    {
        var sut = CreateService();
        const string url = "https://example.com/files/movie.torrent";

        await sut.AddMagnetAsync(url, cancellationToken: Ct);

        _clientMock.Verify(
            c => c.AddTorrentUrlAsync(url, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AddMagnetAsync_UsesFolderPathOverride()
    {
        var sut = CreateService(downloadFolder: "/media/incoming");

        await sut.AddMagnetAsync(
            "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567", "/media/other", Ct);

        _clientMock.Verify(
            c => c.AddTorrentUrlAsync(It.IsAny<string>(), "/media/other", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a link")]
    [InlineData("ftp://example.com/movie.torrent")]
    [InlineData("magnet:")]
    [InlineData("magnet:?dn=NoHashHere")]
    public async Task AddMagnetAsync_RejectsInvalidLinks(string? magnetLink)
    {
        var sut = CreateService();

        await Assert.ThrowsAsync<TorrentValidationException>(
            () => sut.AddMagnetAsync(magnetLink, cancellationToken: Ct));

        _clientMock.Verify(
            c => c.AddTorrentUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AddMagnetAsync_ThrowsWhenNotConfigured()
    {
        var sut = CreateService(url: null);

        await Assert.ThrowsAsync<QbittorrentNotConfiguredException>(
            () => sut.AddMagnetAsync(
                "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567", cancellationToken: Ct));
    }

    [Fact]
    public async Task GetTorrentsAsync_ReturnsClientTorrents()
    {
        var expected = new List<TorrentInfo>
        {
            new(
                Hash: "abc",
                Name: "Some Movie",
                State: "downloading",
                Status: "Downloading",
                Progress: 0.42,
                SizeBytes: 1000,
                DownloadedBytes: 420,
                AmountLeftBytes: 580,
                DownloadSpeed: 1024,
                UploadSpeed: 12,
                EtaSeconds: 60,
                SavePath: "/media",
                AddedOnUnixSeconds: 1_700_000_000)
        };

        _clientMock
            .Setup(c => c.GetTorrentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var sut = CreateService();

        var result = await sut.GetTorrentsAsync(Ct);

        Assert.Single(result);
        Assert.Equal("Some Movie", result[0].Name);
        Assert.Equal(0.42, result[0].Progress, 3);
        Assert.True(result[0].IsDownloading);
    }

    [Fact]
    public async Task GetTorrentsAsync_ThrowsWhenNotConfigured()
    {
        var sut = CreateService(url: null);

        await Assert.ThrowsAsync<QbittorrentNotConfiguredException>(() => sut.GetTorrentsAsync(Ct));
    }

    [Fact]
    public void TorrentInfo_IsNotDownloadingWhenComplete()
    {
        var info = new TorrentInfo(
            Hash: "abc",
            Name: "Done",
            State: "uploading",
            Status: "Seeding",
            Progress: 1d,
            SizeBytes: 1000,
            DownloadedBytes: 1000,
            AmountLeftBytes: 0,
            DownloadSpeed: 0,
            UploadSpeed: 512,
            EtaSeconds: null,
            SavePath: "/media",
            AddedOnUnixSeconds: 1_700_000_000);

        Assert.False(info.IsDownloading);
    }

}
