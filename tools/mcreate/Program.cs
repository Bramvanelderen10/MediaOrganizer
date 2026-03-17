if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: mcreate <source> <target>");
    return 1;
}

var source = Path.GetFullPath(args[0]);
var target = Path.GetFullPath(args[1]);

if (!Directory.Exists(source))
{
    Console.Error.WriteLine($"Source directory does not exist: {source}");
    return 1;
}

if (File.Exists(target))
{
    Console.Error.WriteLine($"Target path exists as a file: {target}");
    return 1;
}

if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
{
    Console.Error.WriteLine($"Target directory must be empty or not exist: {target}");
    return 1;
}

ReplicateStructure(source, target);

Console.WriteLine("Done.");
return 0;

static void ReplicateStructure(string source, string target)
{
    Directory.CreateDirectory(target);

    foreach (var file in Directory.EnumerateFiles(source))
    {
        var destFile = Path.Combine(target, Path.GetFileName(file));
        File.Create(destFile).Dispose();
        Console.WriteLine($"  {destFile}");
    }

    foreach (var dir in Directory.EnumerateDirectories(source))
    {
        var destDir = Path.Combine(target, Path.GetFileName(dir));
        ReplicateStructure(dir, destDir);
    }
}
