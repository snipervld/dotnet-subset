using Nimbleways.Tools.Subset.Helpers;
using Nimbleways.Tools.Subset.Models;
using Nimbleways.Tools.Subset.Utils;

using static Nimbleways.Tools.Subset.Helpers.DotnetSubsetRunner;

namespace Nimbleways.Tools.Subset;

[UsesVerify]
public class CopyFunctionalTests : IDisposable
{
    private static readonly IReadOnlyCollection<TestDescriptor> AllTestDescriptors = TestHelpers.GetTestDescriptors();

    private readonly DisposableTempDirectory _tempDirectory = new();
    private bool _disposedValue;

    private DirectoryInfo OutputDirectory => _tempDirectory.Value;

    public static IEnumerable<object[]> GetCopyTestDescriptors()
    {
        object[][] objects = AllTestDescriptors
            .OfType<CopyTestDescriptor>()
            .Select(ctd => new object[] { ctd }).ToArray();
        return objects;
    }

    [Theory]
    [MemberData(nameof(GetCopyTestDescriptors))]
    public async Task RunCopyTests(CopyTestDescriptor copyTestDescriptor)
    {
        DescriptorExecutionResult result = AssertDescriptor(copyTestDescriptor, OutputDirectory);
        if (copyTestDescriptor.ExitCode == 0)
        {
            Assert.True(DirectoryDiff.AreDirectoriesIdentical(copyTestDescriptor.ExpectedDirectory, OutputDirectory));
        }
        await result.VerifyOutput();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _tempDirectory.Dispose();
            }
            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
