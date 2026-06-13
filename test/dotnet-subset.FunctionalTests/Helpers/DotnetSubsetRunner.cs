using Nimbleways.Tools.Subset.Models;
using Nimbleways.Tools.Subset.Utils.Processes;

namespace Nimbleways.Tools.Subset.Helpers;

internal static class DotnetSubsetRunner
{
    public static DescriptorExecutionResult AssertDescriptor(TestDescriptor descriptor, DirectoryInfo output, int? overriddenExpectedExitCode = null, bool noLogo = true)
    {
        DescriptorExecutionResult executionResult = Run(descriptor, output, noLogo);
        Assert.Equal(overriddenExpectedExitCode ?? descriptor.ExitCode, executionResult.ExitCode);
        return executionResult;
    }

    public static ExecutionResult Run(string[] subsetArgs, DirectoryInfo workingDirectory)
    {
        return MustRunAsDotnetTool()
            ? RunProcess(subsetArgs, workingDirectory)
            : RunMain(subsetArgs, workingDirectory);
    }

    private static DescriptorExecutionResult Run(TestDescriptor descriptor, DirectoryInfo output, bool noLogo)
    {
        var subsetArgs = GetSubsetArgs(descriptor, output, noLogo);
        DirectoryInfo workingDirectory = descriptor.Root;
        ExecutionResult result = Run(subsetArgs, workingDirectory);
        return new DescriptorExecutionResult(descriptor, output, result.ExitCode, result.ConsoleOutput, isOutOfProcess: result.IsOutOfProcess);
    }

    private static string[] GetSubsetArgs(TestDescriptor descriptor, DirectoryInfo output, bool noLogo)
    {
        string projectOrSolution = Path.Combine(descriptor.Root.FullName, descriptor.ProjectOrSolution);
        var args = new[]
        {
            descriptor.OperationName,
            projectOrSolution,
            "--output",
            output.FullName,
            "--root-directory",
            descriptor.Root.FullName
        };
        if (noLogo)
        {
            args = args.Append("--nologo").ToArray();
        }
        return args;
    }

    private static bool MustRunAsDotnetTool()
    {
        string? envVar = Environment.GetEnvironmentVariable("DOTNET_SUBSET_TESTS_RUN_AS_DOTNET_TOOL");
        return string.Equals(envVar, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly object WorkingDirLock = new();

    private static ExecutionResult RunMain(string[] subsetArgs, DirectoryInfo workingDirectory)
    {
        lock (WorkingDirLock)
        {
            using var processSimulator = new ProcessSimulator(workingDirectory);
            int exitCode = Program.Main(subsetArgs);
            return new(exitCode, processSimulator.ConsoleOutput, isOutOfProcess: false);
        }
    }

    private static ExecutionResult RunProcess(IEnumerable<string> subsetArgs, DirectoryInfo workingDirectory)
    {
        string subsetArgsString = string.Join(" ", subsetArgs.Select(a => $@"""{a}"""));

        using ProcessRunner processRunner = new(workingDirectory, "dotnet", $"subset {subsetArgsString}");

        ProcessResult processResult = processRunner.Run(TimeSpan.FromSeconds(30));

        return processResult switch
        {
            ProcessExitedResult result => new(result.ExitCode, result.Output, isOutOfProcess: true),
            ProcessFailureResult { Exception: var exception } => throw exception,
            _ => throw new NotSupportedException()
        };
    }

    private sealed class ProcessSimulator : IDisposable
    {
        private static bool s_canSetWindowWidth = true;

        private readonly int? _originalWindowWidth;
        private readonly string _originalWorkingDirectory;
        private readonly StringWriter _writer;
        private readonly TextWriter _originalOutTextWriter;
        private readonly TextWriter _originalErrorTextWriter;

        public ProcessSimulator(DirectoryInfo workingDirectory)
        {
            _originalWorkingDirectory = Environment.CurrentDirectory;
            Environment.CurrentDirectory = workingDirectory.FullName;
            (_originalOutTextWriter, _originalErrorTextWriter) = (Console.Out, Console.Error);
            _writer = new();
            Console.SetOut(_writer);
            Console.SetError(_writer);
            SetWindowWidth(out _originalWindowWidth);
        }

        public string ConsoleOutput => _writer.ToString();

        public void Dispose()
        {
            Console.SetOut(_originalOutTextWriter);
            Console.SetError(_originalErrorTextWriter);
            _writer.Dispose();
            Environment.CurrentDirectory = _originalWorkingDirectory;
            SetWindowWidth(out var _, _originalWindowWidth);
        }

        private static void SetWindowWidth(out int? previousWindowWidth, int? newWindowWidth = null)
        {
            previousWindowWidth = null;
            if (!OperatingSystem.IsWindows() || !s_canSetWindowWidth)
            {
                return;
            }

            try
            {
                previousWindowWidth = Console.WindowWidth;
                Console.WindowWidth = newWindowWidth ?? Console.LargestWindowWidth;
            }
            // Console.WindowWidth getter fails in Visual Studio on Windows
            catch (IOException)
            {
                // Avoid retrying for the next calls
                s_canSetWindowWidth = false;
            }
        }
    }
}
