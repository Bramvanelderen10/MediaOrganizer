using MediaOrganizer.MoveHistory;
using MediaOrganizer.MovePlan;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace MediaOrganizer.Tests;

public class MovePlanBuilderTests : IDisposable
{
    private readonly Mock<ILogger<MovePlanBuilder>> _loggerMock;
    private readonly Mock<IDbContextFactory<MoveHistoryDbContext>> _contextFactoryMock;
    private readonly DbContextOptions<MoveHistoryDbContext> _dbOptions;
    private MovePlanBuilder _sut;

    public MovePlanBuilderTests()
    {
        _loggerMock = new Mock<ILogger<MovePlanBuilder>>();
        _contextFactoryMock = new Mock<IDbContextFactory<MoveHistoryDbContext>>();

        _dbOptions = new DbContextOptionsBuilder<MoveHistoryDbContext>()
            .UseInMemoryDatabase(databaseName: $"MoveHistoryDb_{Guid.NewGuid()}")
            .Options;

        _contextFactoryMock
            .Setup(f => f.CreateDbContext())
            .Returns(() => new MoveHistoryDbContext(_dbOptions));

        _sut = new MovePlanBuilder(_loggerMock.Object, _contextFactoryMock.Object);
    }

    private MoveHistoryDbContext CreateAssertContext() => new(_dbOptions);

    public void Dispose()
    {
    }

    [Fact]
    public void BuildMovePlan_NewFile_CreatesHistoryEntry()
    {
        // Arrange
        var testFile = "/media/Movie.mp4";
        var rootFolder = "/organized";
        var uniqueKey = "unique-key-123";

        var mediaObject = new MediaObject
        {
            Name = "Test Movie",
            Type = MediaType.Movie,
            MoviePath = testFile
        };

        var mediaObjects = new List<MediaObject> { mediaObject };

        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        using var assertContext = CreateAssertContext();
        var entry = assertContext.MoveHistory.FirstOrDefault(e => e.OriginalFilePath == testFile);
        Assert.NotNull(entry);
        Assert.False(entry.IsMoved);
    }

    [Fact]
    public void BuildMovePlan_FileDoesNotExist_StillCreatesHistoryEntry()
    {
        // Arrange – BuildMovePlan does not check file existence on disk;
        // it creates a history entry regardless.
        var testFile = "/media/nonexistent.mp4";
        var rootFolder = "/organized";

        var mediaObject = new MediaObject
        {
            Name = "Test Movie",
            Type = MediaType.Movie,
            MoviePath = testFile
        };

        var mediaObjects = new List<MediaObject> { mediaObject };

        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        using var assertContext = CreateAssertContext();
        Assert.Single(assertContext.MoveHistory);
    }

    [Fact]
    public void BuildMovePlan_AlreadyMovedSuccessfully_IgnoresFile()
    {
        // Arrange
        var testFile = "/media/Movie.mp4";
        var rootFolder = "/organized";
        var destinationPath = "/organized/Test Movie/Test Movie.mp4";
        var uniqueKey = "Test Movie";

        var mediaObject = new MediaObject
        {
            Name = "Test Movie",
            Type = MediaType.Movie,
            MoviePath = testFile
        };

        var mediaObjects = new List<MediaObject> { mediaObject };

        var existingEntry = new MoveHistoryEntry
        {
            UniqueKey = uniqueKey,
            OriginalFilePath = testFile,
            TargetFilePath = destinationPath,
            IsMoved = true
        };

        using (var seedContext = CreateAssertContext())
        {
            seedContext.MoveHistory.Add(existingEntry);
            seedContext.SaveChanges();
        }

        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        using var assertContext = CreateAssertContext();
        Assert.Single(assertContext.MoveHistory);
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("already moved successfully")),
                It.IsAny<Exception>(),
                It.IsAny<System.Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void BuildMovePlan_UnmovedFileWithSameDestination_KeepsInDatabase()
    {
        // Arrange
        var testFile = "/media/Movie.mp4";
        var rootFolder = "/organized";
        var destinationPath = "/organized/Test Movie/Test Movie.mp4";
        var uniqueKey = "Test Movie";

        var mediaObject = new MediaObject
        {
            Name = "Test Movie",
            Type = MediaType.Movie,
            MoviePath = testFile
        };

        var mediaObjects = new List<MediaObject> { mediaObject };

        var existingEntry = new MoveHistoryEntry
        {
            UniqueKey = uniqueKey,
            OriginalFilePath = testFile,
            TargetFilePath = destinationPath,
            IsMoved = false
        };

        using (var seedContext = CreateAssertContext())
        {
            seedContext.MoveHistory.Add(existingEntry);
            seedContext.SaveChanges();
        }

        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        using var assertContext = CreateAssertContext();
        Assert.Single(assertContext.MoveHistory);
    }

    [Fact]
    public void BuildMovePlan_DestinationPathChanged_CreatesNewRecord()
    {
        // Arrange
        var testFile = "/media/Movie.mp4";
        var rootFolder = "/organized";
        var oldDestination = "/old/Test Movie/Test Movie.mp4";
        var newDestination = "/organized/Test Movie/Test Movie.mp4";
        var uniqueKey = "Test Movie";

        var mediaObject = new MediaObject
        {
            Name = "Test Movie",
            Type = MediaType.Movie,
            MoviePath = testFile
        };

        var mediaObjects = new List<MediaObject> { mediaObject };

        var oldEntry = new MoveHistoryEntry
        {
            UniqueKey = uniqueKey,
            OriginalFilePath = testFile,
            TargetFilePath = oldDestination,
            IsMoved = false
        };

        using (var seedContext = CreateAssertContext())
        {
            seedContext.MoveHistory.Add(oldEntry);
            seedContext.SaveChanges();
        }

        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        using var assertContext = CreateAssertContext();
        var newEntry = assertContext.MoveHistory.FirstOrDefault(e =>
            e.UniqueKey == uniqueKey &&
            e.TargetFilePath == newDestination &&
            !e.IsMoved);
        Assert.NotNull(newEntry);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Destination path changed")),
                It.IsAny<Exception>(),
                It.IsAny<System.Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void BuildMovePlan_ShowWithMultipleEpisodes_CreatesRecordsForEachEpisode()
    {
        // Arrange
        var testFile1 = "/media/Show.S01E01.mkv";
        var testFile2 = "/media/Show.S01E02.mkv";
        var rootFolder = "/organized";

        var season = new Season(1, new List<Episode>
        {
            new(testFile1, 1),
            new(testFile2, 2)
        });
        var mediaObject = new MediaObject
        {
            Name = "Test Show",
            Type = MediaType.Show,
            Seasons = [season]
        };

        var mediaObjects = new List<MediaObject> { mediaObject };


        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        using var assertContext = CreateAssertContext();
        Assert.Equal(2, assertContext.MoveHistory.Count());
    }

    [Fact]
    public void BuildMovePlan_MovieWithCorrectDestination_CreatesCorrectPath()
    {
        // Arrange
        var testFile = "/media/Inception.2010.mp4";
        var rootFolder = "/organized";
        var expectedDestination = "/organized/Inception 2010/Inception 2010.mp4";
        var uniqueKey = "unique-key-123";

        var mediaObject = new MediaObject
        {
            Name = "Inception 2010",
            Type = MediaType.Movie,
            MoviePath = testFile
        };

        var mediaObjects = new List<MediaObject> { mediaObject };

        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        using var assertContext = CreateAssertContext();
        var entry = assertContext.MoveHistory.FirstOrDefault(e =>
            e.TargetFilePath == expectedDestination);
        Assert.NotNull(entry);
    }

    [Fact]
    public void BuildMovePlan_ShowEpisodeWithCorrectDestination_CreatesCorrectPath()
    {
        // Arrange
        var testFile = "/media/Breaking.Bad.S02E05.mkv";
        var rootFolder = "/organized";
        var expectedDestination = "/organized/Breaking Bad/Season 02/Breaking.Bad.S02E05.mkv";
        var uniqueKey = "unique-key-456";

        var season = new Season(2, new List<Episode> { new(testFile, 5) });
        var mediaObject = new MediaObject
        {
            Name = "Breaking Bad",
            Type = MediaType.Show,
            Seasons = [season]
        };

        var mediaObjects = new List<MediaObject> { mediaObject };


        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        using var assertContext = CreateAssertContext();
        var entry = assertContext.MoveHistory.FirstOrDefault(e =>
            e.TargetFilePath == expectedDestination);
        Assert.NotNull(entry);
    }

    [Fact]
    public void BuildMovePlan_EmptyMediaObjectList_SavesChangesWithoutAddingRecords()
    {
        // Arrange
        var mediaObjects = new List<MediaObject>();

        // Act
        _sut.BuildMovePlan(mediaObjects, "/organized");

        // Assert
        using var assertContext = CreateAssertContext();
        Assert.Empty(assertContext.MoveHistory);
    }

    [Fact]
    public void BuildMovePlan_MultipleMediaObjects_ProcessesAll()
    {
        // Arrange
        var movieFile = "/media/Movie.mp4";
        var showFile1 = "/media/Show.S01E01.mkv";
        var showFile2 = "/media/Show.S01E02.mkv";
        var rootFolder = "/organized";

        var movieObject = new MediaObject
        {
            Name = "Test Movie",
            Type = MediaType.Movie,
            MoviePath = movieFile
        };

        var season = new Season(1, new List<Episode>
        {
            new(showFile1, 1),
            new(showFile2, 2)
        });
        var showObject = new MediaObject
        {
            Name = "Test Show",
            Type = MediaType.Show,
            Seasons = [season]
        };

        var mediaObjects = new List<MediaObject> { movieObject, showObject };


        // Act
        _sut.BuildMovePlan(mediaObjects, rootFolder);

        // Assert
        using var assertContext = CreateAssertContext();
        Assert.Equal(3, assertContext.MoveHistory.Count());
    }
}
