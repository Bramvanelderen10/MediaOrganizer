using MediaOrganizer.Helpers;
using MediaOrganizer.History;
using MediaOrganizer.Orchestration;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace MediaOrganizer.Tests;

public class MediaFileRestorerTests : IDisposable
{
    private readonly Mock<ILogger<MediaFileRestorer>> _loggerMock = new();
    private readonly Mock<IFileSystem> _fsMock = new();
    private readonly DbContextOptions<MoveHistoryDbContext> _dbOptions;
    private readonly Mock<IDbContextFactory<MoveHistoryDbContext>> _contextFactoryMock;
    private readonly MoveHistoryStore _moveHistoryStore;
    private readonly MediaFileRestorer _sut;

    public MediaFileRestorerTests()
    {
        _dbOptions = new DbContextOptionsBuilder<MoveHistoryDbContext>()
            .UseInMemoryDatabase($"RestoreDb_{Guid.NewGuid()}")
            .Options;

        _contextFactoryMock = new Mock<IDbContextFactory<MoveHistoryDbContext>>();
        _contextFactoryMock.Setup(f => f.CreateDbContext())
            .Returns(() => new MoveHistoryDbContext(_dbOptions));

        _moveHistoryStore = new MoveHistoryStore(
            Mock.Of<ILogger<MoveHistoryStore>>(),
            _contextFactoryMock.Object);

        _sut = new MediaFileRestorer(_loggerMock.Object, _fsMock.Object, _moveHistoryStore);
    }

    public void Dispose() { }

    [Fact]
    public async Task RestoreAllAsync_ReturnsZeroCounts_WhenNoPendingRestores()
    {
        var result = await _sut.RestoreAllAsync();

        Assert.Equal(0, result.TotalPendingFiles);
        Assert.Equal(0, result.RestoredFiles);
        Assert.Equal(0, result.SkippedFiles);
    }

    [Fact]
    public async Task RestoreAllAsync_SkipsWhenTargetFileDoesNotExist()
    {
        SeedMovedEntry("/source/movie.mp4", "/dest/Movie/Movie.mp4");
        _fsMock.Setup(f => f.FileExists("/dest/Movie/Movie.mp4")).Returns(false);

        var result = await _sut.RestoreAllAsync();

        Assert.Equal(1, result.TotalPendingFiles);
        Assert.Equal(0, result.RestoredFiles);
        Assert.Equal(1, result.SkippedFiles);
        _fsMock.Verify(f => f.MoveFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RestoreAllAsync_SkipsWhenOriginalPathAlreadyExists()
    {
        SeedMovedEntry("/source/movie.mp4", "/dest/Movie/Movie.mp4");
        _fsMock.Setup(f => f.FileExists("/dest/Movie/Movie.mp4")).Returns(true);
        _fsMock.Setup(f => f.FileExists("/source/movie.mp4")).Returns(true);

        var result = await _sut.RestoreAllAsync();

        Assert.Equal(1, result.TotalPendingFiles);
        Assert.Equal(0, result.RestoredFiles);
        Assert.Equal(1, result.SkippedFiles);
    }

    [Fact]
    public async Task RestoreAllAsync_MovesFileAndUpdatesHistory()
    {
        SeedMovedEntry("/source/movie.mp4", "/dest/Movie/Movie.mp4");
        _fsMock.Setup(f => f.FileExists("/dest/Movie/Movie.mp4")).Returns(true);
        _fsMock.Setup(f => f.FileExists("/source/movie.mp4")).Returns(false);

        var result = await _sut.RestoreAllAsync();

        Assert.Equal(1, result.TotalPendingFiles);
        Assert.Equal(1, result.RestoredFiles);
        Assert.Equal(0, result.SkippedFiles);
        _fsMock.Verify(f => f.CreateDirectory("/source"), Times.Once);
        _fsMock.Verify(f => f.MoveFile("/dest/Movie/Movie.mp4", "/source/movie.mp4"), Times.Once);

        // Verify history was updated
        using var ctx = new MoveHistoryDbContext(_dbOptions);
        var entry = ctx.MoveHistory.First();
        Assert.False(entry.IsMoved);
    }

    [Fact]
    public async Task RestoreAllAsync_ProcessesMultipleEntries()
    {
        SeedMovedEntry("/source/a.mp4", "/dest/A/A.mp4");
        SeedMovedEntry("/source/b.mkv", "/dest/B/B.mkv");

        _fsMock.Setup(f => f.FileExists("/dest/A/A.mp4")).Returns(true);
        _fsMock.Setup(f => f.FileExists("/source/a.mp4")).Returns(false);
        _fsMock.Setup(f => f.FileExists("/dest/B/B.mkv")).Returns(true);
        _fsMock.Setup(f => f.FileExists("/source/b.mkv")).Returns(false);

        var result = await _sut.RestoreAllAsync();

        Assert.Equal(2, result.TotalPendingFiles);
        Assert.Equal(2, result.RestoredFiles);
    }

    // ────────────── Helpers ──────────────

    private void SeedMovedEntry(string original, string target)
    {
        using var ctx = new MoveHistoryDbContext(_dbOptions);
        ctx.MoveHistory.Add(new MoveHistoryEntry
        {
            UniqueKey = Path.GetFileNameWithoutExtension(original),
            OriginalFilePath = original,
            TargetFilePath = target,
            IsMoved = true,
        });
        ctx.SaveChanges();
    }
}
