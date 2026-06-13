using Common;

using Microsoft.Build.Evaluation;

namespace Nimbleways.Tools.Subset.Commands;

internal sealed class RestoreSubset : SubsetCommandBase
{
    protected override IEnumerable<FileInfo> GetFilesToCopy(FileInfo projectOrSolution, DirectoryInfo rootFolder)
    {
        using var projectCollection = new ProjectCollection();
        var projectsByFullPath = new Dictionary<string, Project>();
        foreach (var project in GetRootProjects(projectOrSolution))
        {
            VisitAllProjects(projectCollection, rootFolder, project, projectsByFullPath);
        }
        var projectListAsString = string.Join(Environment.NewLine + " - ", projectsByFullPath.Keys.OrderBy(f => f));
        Console.WriteLine($"Found {projectsByFullPath.Count} project(s) to copy:{Environment.NewLine + " - "}{projectListAsString}");
        Console.WriteLine();
        var nugetConfigFiles = GetNugetConfigFiles(rootFolder, projectsByFullPath);
        var extraFilesInvolvedInRestore = projectsByFullPath.Values
            .SelectMany(project => GetExtraFilesInvolvedInRestore(rootFolder, project))
            .Concat(nugetConfigFiles)
            .Distinct(FileInfoComparer.Instance)
            .ToList();
        if (IsSolutionFile(projectOrSolution))
        {
            extraFilesInvolvedInRestore.Add(projectOrSolution);
        }
        if (extraFilesInvolvedInRestore.Count > 0)
        {
            var extraFilesInvolvedInRestoreAsString = string.Join(Environment.NewLine + " - ", extraFilesInvolvedInRestore.Select(f => f.FullName).OrderBy(f => f));
            Console.WriteLine($"Found {extraFilesInvolvedInRestore.Count} extra file(s) to copy:{Environment.NewLine + " - "}{extraFilesInvolvedInRestoreAsString}");
            Console.WriteLine();
        }
        return projectsByFullPath.Keys
            .Select(fullPath => new FileInfo(fullPath))
            .Concat(extraFilesInvolvedInRestore)
            .Distinct(FileInfoComparer.Instance);
    }
}
