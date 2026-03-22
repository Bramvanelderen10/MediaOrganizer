using MediaOrganizer.Configuration;
using MediaOrganizer.Helpers;

namespace MediaOrganizer.Endpoints;

public static class FileManagementEndpoints
{
    public static void MapFileManagementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/browse", (IFileSystem fileSystem, Microsoft.Extensions.Options.IOptions<MediaOrganizerOptions> options, string? path) =>
        {
            var opts = options.Value;
            var root = opts.SourceFolder;
            if (string.IsNullOrWhiteSpace(root) || !fileSystem.DirectoryExists(root))
            {
                return Results.BadRequest(new { message = "Source folder is not configured or does not exist." });
            }

            root = Path.GetFullPath(root);
            var target = string.IsNullOrWhiteSpace(path) ? root : Path.GetFullPath(Path.Combine(root, path));

            if (!target.StartsWith(root, StringComparison.Ordinal))
            {
                return Results.BadRequest(new { message = "Path is outside the source folder." });
            }

            if (!fileSystem.DirectoryExists(target))
            {
                return Results.NotFound(new { message = "Directory not found." });
            }

            var directories = fileSystem.EnumerateDirectories(target)
                .Select(d => new { name = Path.GetFileName(d), path = Path.GetRelativePath(root, d) })
                .OrderBy(d => d.name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var files = fileSystem.EnumerateFiles(target, "*", SearchOption.TopDirectoryOnly)
                .Select(f => new
                {
                    name = Path.GetFileName(f),
                    path = Path.GetRelativePath(root, f),
                    size = new FileInfo(f).Length
                })
                .OrderBy(f => f.name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Results.Ok(new
            {
                currentPath = Path.GetRelativePath(root, target),
                directories,
                files
            });
        })
        .WithName("Browse")
        .WithSummary("Lists directory contents under the source folder")
        .WithDescription("Optional query parameter: ?path=sub/dir relative to the source root. Returns folders and files.");

        app.MapPost("/rename", (IFileSystem fileSystem, Microsoft.Extensions.Options.IOptions<MediaOrganizerOptions> options, RenameRequest request) =>
        {
            var opts = options.Value;
            var root = opts.SourceFolder;
            if (string.IsNullOrWhiteSpace(root) || !fileSystem.DirectoryExists(root))
            {
                return Results.BadRequest(new { message = "Source folder is not configured or does not exist." });
            }

            if (string.IsNullOrWhiteSpace(request.Path) || string.IsNullOrWhiteSpace(request.NewName))
            {
                return Results.BadRequest(new { message = "path and newName are required." });
            }

            if (request.NewName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return Results.BadRequest(new { message = "newName contains invalid characters." });
            }

            root = Path.GetFullPath(root);
            var fullPath = Path.GetFullPath(Path.Combine(root, request.Path));

            if (!fullPath.StartsWith(root, StringComparison.Ordinal))
            {
                return Results.BadRequest(new { message = "Path is outside the source folder." });
            }

            var parentDir = Path.GetDirectoryName(fullPath)!;
            var newFullPath = Path.Combine(parentDir, request.NewName);

            if (!newFullPath.StartsWith(root, StringComparison.Ordinal))
            {
                return Results.BadRequest(new { message = "New path would be outside the source folder." });
            }

            if (fileSystem.FileExists(fullPath))
            {
                if (fileSystem.FileExists(newFullPath))
                {
                    return Results.Conflict(new { message = "A file with that name already exists." });
                }
                fileSystem.MoveFile(fullPath, newFullPath);
            }
            else if (fileSystem.DirectoryExists(fullPath))
            {
                if (fileSystem.DirectoryExists(newFullPath))
                {
                    return Results.Conflict(new { message = "A directory with that name already exists." });
                }
                Directory.Move(fullPath, newFullPath);
            }
            else
            {
                return Results.NotFound(new { message = "File or directory not found." });
            }

            return Results.Ok(new
            {
                message = "Renamed successfully",
                oldPath = request.Path,
                newPath = Path.GetRelativePath(root, newFullPath)
            });
        })
        .WithName("Rename")
        .WithSummary("Renames a file or directory under the source folder");

        app.MapPost("/move", (IFileSystem fileSystem, Microsoft.Extensions.Options.IOptions<MediaOrganizerOptions> options, MoveItemRequest request) =>
        {
            var opts = options.Value;
            var root = opts.SourceFolder;
            if (string.IsNullOrWhiteSpace(root) || !fileSystem.DirectoryExists(root))
            {
                return Results.BadRequest(new { message = "Source folder is not configured or does not exist." });
            }

            if (string.IsNullOrWhiteSpace(request.SourcePath) || string.IsNullOrWhiteSpace(request.DestinationFolder))
            {
                return Results.BadRequest(new { message = "sourcePath and destinationFolder are required." });
            }

            root = Path.GetFullPath(root);
            var srcFull = Path.GetFullPath(Path.Combine(root, request.SourcePath));
            var destDir = Path.GetFullPath(Path.Combine(root, request.DestinationFolder));

            if (!srcFull.StartsWith(root, StringComparison.Ordinal) || !destDir.StartsWith(root, StringComparison.Ordinal))
            {
                return Results.BadRequest(new { message = "Paths must be within the source folder." });
            }

            if (!fileSystem.DirectoryExists(destDir))
            {
                return Results.NotFound(new { message = "Destination directory not found." });
            }

            var fileName = Path.GetFileName(srcFull);
            var destFull = Path.Combine(destDir, fileName);

            if (fileSystem.FileExists(srcFull))
            {
                if (fileSystem.FileExists(destFull))
                {
                    return Results.Conflict(new { message = "A file with that name already exists in the destination." });
                }
                fileSystem.MoveFile(srcFull, destFull);
            }
            else if (fileSystem.DirectoryExists(srcFull))
            {
                if (fileSystem.DirectoryExists(destFull))
                {
                    return Results.Conflict(new { message = "A directory with that name already exists in the destination." });
                }
                Directory.Move(srcFull, destFull);
            }
            else
            {
                return Results.NotFound(new { message = "Source file or directory not found." });
            }

            return Results.Ok(new
            {
                message = "Moved successfully",
                sourcePath = request.SourcePath,
                newPath = Path.GetRelativePath(root, destFull)
            });
        })
        .WithName("MoveItem")
        .WithSummary("Moves a file or directory to a different folder under the source root");

        app.MapPost("/delete", (IFileSystem fileSystem, Microsoft.Extensions.Options.IOptions<MediaOrganizerOptions> options, DeleteRequest request) =>
        {
            var opts = options.Value;
            var root = opts.SourceFolder;
            if (string.IsNullOrWhiteSpace(root) || !fileSystem.DirectoryExists(root))
            {
                return Results.BadRequest(new { message = "Source folder is not configured or does not exist." });
            }

            if (request.Paths is null || request.Paths.Length == 0)
            {
                return Results.BadRequest(new { message = "paths array is required and must not be empty." });
            }

            root = Path.GetFullPath(root);
            var deleted = 0;
            var errors = new List<string>();

            foreach (var p in request.Paths)
            {
                if (string.IsNullOrWhiteSpace(p))
                {
                    errors.Add("Empty path skipped.");
                    continue;
                }

                var fullPath = Path.GetFullPath(Path.Combine(root, p));

                if (!fullPath.StartsWith(root, StringComparison.Ordinal) || fullPath == root)
                {
                    errors.Add($"{p}: cannot delete (outside source or is source root).");
                    continue;
                }

                try
                {
                    if (fileSystem.FileExists(fullPath))
                    {
                        fileSystem.DeleteFile(fullPath);
                        deleted++;
                    }
                    else if (fileSystem.DirectoryExists(fullPath))
                    {
                        fileSystem.DeleteDirectory(fullPath, recursive: true);
                        deleted++;
                    }
                    else
                    {
                        errors.Add($"{p}: not found.");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{p}: {ex.Message}");
                }
            }

            return Results.Ok(new
            {
                message = "Delete completed",
                deletedCount = deleted,
                errors
            });
        })
        .WithName("Delete")
        .WithSummary("Deletes one or more files or directories under the source folder")
        .WithDescription("Accepts an array of relative paths. Directories are deleted recursively.");
    }
}
