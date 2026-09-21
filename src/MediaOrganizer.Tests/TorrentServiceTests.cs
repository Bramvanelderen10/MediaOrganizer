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

}
