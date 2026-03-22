# mcreate

`mcreate` is a .NET CLI tool that recreates a source folder tree in a target location using empty placeholder files.

## What it does

- Recursively walks the source directory
- Creates the target directory if it does not exist
- Recreates the full folder structure in the target
- Creates zero-byte files matching the source file names
- Refuses to run if the target already exists and is not empty

## Usage

```bash
dotnet run --project tools/mcreate -- /path/to/source /path/to/target
```

## Pack the tool

```bash
dotnet pack tools/mcreate/mcreate.csproj
```

The package is written to `tools/mcreate/nupkg`.

## Install globally

```bash
dotnet tool install --global --add-source tools/mcreate/nupkg mcreate
```

## Update after changes

```bash
dotnet tool update --global --add-source tools/mcreate/nupkg mcreate
```

## Run after install

```bash
mcreate /path/to/source /path/to/target
```

## Notes

- The source directory must exist.
- The target path must not point to an existing file.
- The target directory must be empty if it already exists.
