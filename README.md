<p align="center">
  <img src="doc/dotnet-subset.png" alt="Image" />
</p>

# dotnet-subset
[![NuGet version (dotnet-subset)](https://img.shields.io/nuget/v/dotnet-subset.svg?style=flat-square)](https://www.nuget.org/packages/dotnet-subset/) [NOTE: temporarily references the original package, not this fork's package]


`dotnet-subset` is a .NET tool that copies a subset of files from a repository to a directory.

The tool is mainly used in Dockerfiles to optimize the docker build caching for "dotnet restore" instructions.

## Motivation

To learn more about the motivation behind `dotnet-subset`, please read [the related blog post](https://blog.nimbleways.com/docker-build-caching-for-dotnet-applications-done-right-with-dotnet-subset/).

## Features
* Two commands: `restore` (files needed to `dotnet restore`) and `copy` (all files from the involved projects, plus everything `restore` would copy).
* Accepts a single project (`.csproj`), a solution (`.sln`), a solution filter (`.slnf`) or the new XML solution format (`.slnx`) as input.
* Copies all the required files for the root projects, including their project dependencies transitively.
* Copies imported MSBuild files, including [Directory.Build.props and Directory.Build.targets](https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-your-build?view=vs-2022#directorybuildprops-and-directorybuildtargets).
* For each required project, copies all NuGet configuration files involved in computing its effective NuGet settings. See [How NuGet settings are applied](https://learn.microsoft.com/en-us/nuget/consume-packages/configuring-nuget-behavior#how-settings-are-applied). `dotnet-subset` also supports [custom NuGet configuration filepath](https://learn.microsoft.com/en-us/nuget/reference/msbuild-targets#examples) defined in the project's csproj.
* For each required project, copies the NuGet lock file and support the `NuGetLockFilePath` property. See `https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#lock-file-extensibility`.
* Only copies files under the specified root, while maintaining their relative path to it.

## Installation
### From NuGet
```
dotnet tool install --global dotnet-subset
```
### From source
Prerequisite: .NET SDK 10.0 or newer

1. Clone this repository
2. Open a terminal in the repository's root
3. `dotnet pack --configuration Release --version-suffix local`
4. `dotnet tool update dotnet-subset --global --prerelease --configfile .config/nuget-local-install-release.config`

## Usage
```
Description:
  Create a subset for the restore operation.

Commands
  - `restore`: creates a subset of the files required to `dotnet restore` (project files, imported MSBuild files, NuGet configs and lock files).
  - `copy`: creates a subset of all files from the involved projects' directories, plus everything `restore` would copy (imports, NuGet configs/lock files, the solution file). So `copy` is a superset of `restore`.

Usage:
  dotnet-subset <command> <projectOrSolution> [options]

Arguments:
  <command>            `restore` or `copy`
  <projectOrSolution>  Project (.csproj), solution (.sln/.slnx) or solution filter (.slnf) to process.

Options:
  --root-directory <root-directory> (REQUIRED)  Directory from where the files will be copied, usually the
                                                repository's root.
  --output <output> (REQUIRED)                  Directory where the subset files will be copied,
                                                preserving the original hierarchy.
  -?, -h, --help                                Show help and usage information
```

Example with a project:
```
dotnet subset restore /source/complexapp/complexapp.csproj --root-directory /source/ --output /tmp/restore_subset/
```
Example with a solution (`.sln` or `.slnx`):
```
dotnet subset restore /source/complexapp.sln --root-directory /source/ --output /tmp/restore_subset/
```
Example with `copy` to grab every file from the involved projects:
```
dotnet subset copy /source/complexapp.sln --root-directory /source/ --output /tmp/copy_subset/
```

### Solution filters (`.slnf`)
A solution filter only defines the **root** projects of the traversal - it does **not** restrict the result to just those projects. The tool still walks each root project's `ProjectReference` recursively and pulls in transitive dependencies, even ones not listed in the filter, as long as they live under `--root-directory`:

```
copied = (projects from the filter) plus (their transitive ProjectReference dependencies under root)
```

### `--root-directory` matters
Everything that ends up in the result must be located under `--root-directory`. If the root is narrower than needed, transitive dependencies whose path is outside the root are skipped, and "upper" imports/configs (`Directory.Build.props`/`.targets`, `nuget.config`) above the root are cut off. The root should cover all dependent projects and all required imports/configs - usually the solution directory, not a single project's directory.

## dotnet-subset + docker
Please check these pull requests to see how to use `dotnet-subset` in your `Dockerfile`:
- https://github.com/othmane-kinane-nw/eShopOnContainers/pull/1/files?diff=unified&w=0
- https://github.com/othmane-kinane-nw/modular-monolith-with-ddd/pull/1/files?diff=unified&w=0
- https://github.com/othmane-kinane-nw/dotnet-docker/pull/1/files?diff=unified&w=0

## dotnet-subset restore + copy + docker
Using both commands together lets you cache **two** layers independently:

* The **restore subset** (`restore`) is small - project files, imports and NuGet configs. It feeds the `dotnet restore` layer, which stays cached until your dependencies change.
* The **build subset** (`copy`) is all the source of the involved projects, transitively. It feeds the build/publish layer, which stays cached until those projects' files change - changes to unrelated projects (tests, other services) don't touch it.

A single up-front `subset` stage extracts both; the later stages each pull in only the subset they need, so each cache is invalidated only by what genuinely affects it. The example below is generalized from a real production `Dockerfile`:

```dockerfile
# syntax=docker/dockerfile:1.7.0-labs

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS subset
WORKDIR /src
# local folder works as a package source, so the tool (or any package) installs without a
# NuGet feed - handy for this fork, which is not published to NuGet as of 2026-06-14.
COPY custom-packages custom-packages
RUN dotnet tool install dotnet-subset --global --no-cache --version 0.4.0 --add-source custom-packages
ENV PATH="$PATH:/root/.dotnet/tools"
COPY . .
RUN dotnet subset restore "./Services/MyApp.Service/MyApp.Service.csproj" --root-directory /src --output /restore_subset
RUN dotnet subset copy    "./Services/MyApp.Service/MyApp.Service.csproj" --root-directory /src --output /build_subset

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# copy shared files (binaries, referenced via HintPath (e.g. ..\lib\), etc.) explicitly - dotnet-subset can't see them.
COPY libs libs
COPY --from=subset /restore_subset/ .
# or use nuget.config - up to you
RUN --mount=type=secret,id=feed_pat \
    dotnet nuget add source \
      "https://pkgs.dev.azure.com/your-org/your-project/_packaging/YourFeed/nuget/v3/index.json" \
      --name YourFeed --username az --password "$(cat /run/secrets/feed_pat)" --store-password-in-clear-text \
    && dotnet restore "./Services/MyApp.Service/MyApp.Service.csproj" -r linux-x64

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
COPY --from=subset /build_subset/ .
WORKDIR "/src/Services/MyApp.Service"
RUN dotnet publish "./MyApp.Service.csproj" -c $BUILD_CONFIGURATION -o /app/publish -r linux-x64 --no-self-contained --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MyApp.Service.dll"]
```

## What this fork adds/changes

This is a fork of [`nimbleways/dotnet-subset`](https://github.com/nimbleways/dotnet-subset). It merges two upstream pull requests and adds a few features on top. Changes compared to the original:

* **`copy` command** (from upstream PR #3, reworked): alongside `restore`, copies every file from the involved projects' directories plus everything `restore` would copy. The command logic is factored into a shared `SubsetCommandBase` with `RestoreSubset` and `CopySubset` deriving from it.
* **Solution filter (`.slnf`) support** (from upstream PR #26): a `.slnf` is parsed for its root projects, then dependencies are resolved transitively (see [Solution filters](#solution-filters-\(.slnf\)) above).
* **XML solution (`.slnx`) support**: the new XML solution format is handled natively, behaving the same as a classic `.sln`.
* **`copy` includes restore files**: `copy` also pulls in the NuGet configs that `restore` copies, so its output stays restore-ready (`copy` is a superset of `restore`).
* **Modernized to .NET 10 / current SDK**: target framework moved to `net10.0`, MSBuild and related packages upgraded, the project switched to a strongly-typed `FileInfo`/`DirectoryInfo` style with dedicated exceptions.
* **Functional tests** covering the `restore`/`copy` commands, `.slnf`/`.slnx` inputs, and the transitive-dependency behavior of solution filters.

**WARNING**: the LLM was used to help me combine two upstream PRs and write new functional tests.

### Behavior notes for the fork's features

A few subtleties of the additions above (`copy`, `.slnf`, `.slnx`), verified empirically while building this fork:

* **The solution file itself is copied for `.sln`/`.slnx`, but not for `.slnf`.** When the input is a solution (`.sln` or `.slnx`), the solution file is added to the output. When the input is a solution filter (`.slnf`), the `.slnf` is not copied - its extension is not `.sln`, so it is not treated as a solution file.
* **Only the projects listed in the solution are traversed.** A project that sits in the directory tree but is not referenced by the solution (and is not a transitive dependency of a listed project) is not copied.
* **`copy` is a strict superset of `restore`** for the same input: it copies every file from the involved projects' directories plus everything `restore` would copy (imports, NuGet configs/lock files, the solution file).
* **`.slnx` is handled natively, `.slnf` is not.** After the MSBuild upgrade, `SolutionFile.Parse` reads `.slnx` correctly and returns the solution's projects, so `.slnx` is treated exactly like `.sln`. The `.slnf` support, taken from upstream [PR #26](https://github.com/nimbleways/dotnet-subset/pull/26), keeps a dedicated JSON parser on purpose: the same `SolutionFile.Parse` expands a `.slnf` to its base `.sln` but does **not** apply the project mask - it returns all projects of the base solution rather than the filtered subset. (Centralized `.slnf` support is still an open request: [vs-solutionpersistence#85](https://github.com/microsoft/vs-solutionpersistence/issues/85).)

## License

Copyright © [Nimbleways](https://www.nimbleways.com/), [Othmane Kinane](https://github.com/othmane-kinane-nw), [snipervld](https://github.com/snipervld) and contributors.

`dotnet-subset` is provided as-is under the MIT license. For more information see [LICENSE](https://github.com/nimbleways/dotnet-subset/blob/main/LICENSE).

* For Microsoft.Build, see https://github.com/dotnet/msbuild/blob/main/LICENSE
* For Microsoft.Build.Locator, see https://github.com/microsoft/MSBuildLocator/blob/master/LICENSE
* For System.CommandLine, see https://github.com/dotnet/command-line-api/blob/main/LICENSE.md
