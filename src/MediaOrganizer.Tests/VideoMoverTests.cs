using MediaOrganizer.Execution;
using MediaOrganizer.Helpers;
using MediaOrganizer.History;

using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace MediaOrganizer.Tests;

public class VideoMoverTests
{
    private readonly Mock<ILogger<VideoMover>> _loggerMock = new();
    private readonly Mock<IFileSystem> _fsMock = new();
    private readonly Mock<SettingStore> _historyStoreMock;
    private readonly VideoMover _sut;

    public VideoMoverTests()
    {
        // MoveHistoryStore requires constructor parameters; we'll use a helper to create a mock.
        _historyStoreMock = CreateMoveHistoryStoreMock();
        _sut = new VideoMover(_loggerMock.Object, _fsMock.Object, _historyStoreMock.Object);
    }

    [Fact]
    public void ExecuteMovePlan_SkipsWhenSourceFileMissing()
    {
        var entry = CreateEntry(1, "/source/movie.mp4", "/dest/Movie/Movie.mp4");
        _fsMock.Setup(f => f.FileExists("/source/movie.mp4")).Returns(false);

        var result = _sut.ExecuteMovePlan(new[] { entry });

        Assert.Empty(result);
        _fsMock.Verify(f => f.MoveFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ExecuteMovePlan_CreatesDestinationDirectoryAndMovesFile()
    {
        var entry = CreateEntry(1, "/source/movie.mp4", "/dest/Movie/Movie.mp4");
        _fsMock.Setup(f => f.FileExists("/source/movie.mp4")).Returns(true);
        _fsMock.Setup(f => f.FileExists("/dest/Movie/Movie.mp4")).Returns(false);

        var result = _sut.ExecuteMovePlan(new[] { entry });

        Assert.Single(result);
        Assert.Equal("/source/movie.mp4", result[0].OriginalPath);
        Assert.Equal("/dest/Movie/Movie.mp4", result[0].DestinationPath);
        _fsMock.Verify(f => f.CreateDirectory("/dest/Movie"), Times.Once);
        _fsMock.Verify(f => f.MoveFile("/source/movie.mp4", "/dest/Movie/Movie.mp4"), Times.Once);
    }

    [Fact]
    public void ExecuteMovePlan_UpdatesHistoryOnSuccess()
    {
        var entry = CreateEntry(42, "/source/movie.mp4", "/dest/Movie/Movie.mp4");
        _fsMock.Setup(f => f.FileExists("/source/movie.mp4")).Returns(true);
        _fsMock.Setup(f => f.FileExists("/dest/Movie/Movie.mp4")).Returns(false);

        _sut.ExecuteMovePlan(new[] { entry });

        _historyStoreMock.Verify(h => h.UpdateIsMoved(42, true), Times.Once);
    }

    [Fact]
    public void ExecuteMovePlan_HandlesUniquePathWhenDestinationExists()
    {
        var entry = CreateEntry(1, "/source/movie.mp4", "/dest/Movie/Movie.mp4");
        _fsMock.Setup(f => f.FileExists("/source/movie.mp4")).Returns(true);
        _fsMock.Setup(f => f.FileExists("/dest/Movie/Movie.mp4")).Returns(true);
        _fsMock.Setup(f => f.FileExists("/dest/Movie/Movie (1).mp4")).Returns(false);

        var result = _sut.ExecuteMovePlan(new[] { entry });

        Assert.Single(result);
        Assert.Equal("/dest/Movie/Movie (1).mp4", result[0].DestinationPath);
        _historyStoreMock.Verify(h => h.UpdateTargetPath(1, "/dest/Movie/Movie (1).mp4"), Times.Once);
        _fsMock.Verify(f => f.MoveFile("/source/movie.mp4", "/dest/Movie/Movie (1).mp4"), Times.Once);
    }

    [Fact]
    public void ExecuteMovePlan_ProcessesMultipleEntries()
    {
        var entries = new[]
        {
            CreateEntry(1, "/source/a.mp4", "/dest/A/A.mp4"),
            CreateEntry(2, "/source/b.mkv", "/dest/B/B.mkv"),
        };

        _fsMock.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);
        _fsMock.Setup(f => f.FileExists("/source/a.mp4")).Returns(true);
        _fsMock.Setup(f => f.FileExists("/source/b.mkv")).Returns(true);

        var result = _sut.ExecuteMovePlan(entries);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ExecuteMovePlan_ReturnsEmptyForEmptyPlan()
    {
        var result = _sut.ExecuteMovePlan(Array.Empty<MoveHistoryEntry>());

        Assert.Empty(result);
    }

    [Fact]
    public void ExecuteMovePlan_SkipsMoveWhenSourceAndDestinationAreSamePath()
    {
        var entry = CreateEntry(1, "/dest/Show/Season 01/Episode.mkv", "/dest/Show/Season 01/Episode.mkv");
        _fsMock.Setup(f => f.FileExists("/dest/Show/Season 01/Episode.mkv")).Returns(true);

        var result = _sut.ExecuteMovePlan(new[] { entry });

        Assert.Single(result);
        Assert.Equal("/dest/Show/Season 01/Episode.mkv", result[0].OriginalPath);
        Assert.Equal("/dest/Show/Season 01/Episode.mkv", result[0].DestinationPath);
        // Should mark as moved without actually calling MoveFile
        _historyStoreMock.Verify(h => h.UpdateIsMoved(1, true), Times.Once);
        _fsMock.Verify(f => f.MoveFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ────────────── Helpers ──────────────

    private static MoveHistoryEntry CreateEntry(long id, string original, string target)
        => new()
        {
            Id = id,
            UniqueKey = Path.GetFileNameWithoutExtension(original),
            OriginalFilePath = original,
            TargetFilePath = target,
            IsMoved = false,
        };

    private static Mock<SettingStore> CreateMoveHistoryStoreMock()
    {
        // MoveHistoryStore has dependencies on ILogger and IDbContextFactory.
        // We create a mock that doesn't call the real constructor.
        var mock = new Mock<SettingStore>(
            MockBehavior.Loose,
            Mock.Of<ILogger<SettingStore>>(),
            (Microsoft.EntityFrameworkCore.IDbContextFactory<MoveHistoryDbContext>)null!);

        mock.CallBase = false;
        return mock;
    }
}
