using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EFCore.AutoSeed.UnitTests.Packaging;

public sealed partial class PackagingTests
{
    [Theory]
    [InlineData("src/AutoSeed.Inference/AutoSeed.Inference.csproj")]
    [InlineData("src/AutoSeed.Cli/AutoSeed.Cli.csproj")]
    public void PublishedCsproj_PacksEveryRelativeImageReferencedByReadme(string relativeCsprojPath)
    {
        string repoRoot = FindRepoRoot();
        string readmeText = File.ReadAllText(Path.Combine(repoRoot, "README.md"));
        List<string> referencedImages = MarkdownImageReference().Matches(readmeText)
            .Select(match => match.Groups["path"].Value)
            .Where(path => !path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                            !path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();

        XDocument csproj = XDocument.Load(Path.Combine(repoRoot, relativeCsprojPath));
        HashSet<string> packedFiles = csproj.Descendants("None")
            .Where(none => (string?)none.Attribute("Pack") == "true")
            .Select(none => ((string?)none.Attribute("Include") ?? "").Split(['\\', '/']).Last())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(referencedImages, image => Assert.Contains(image, packedFiles));
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EFCore.AutoSeed.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }

    [GeneratedRegex(@"!\[[^\]]*\]\((?<path>[^)\s]+)\)")]
    private static partial Regex MarkdownImageReference();
}
