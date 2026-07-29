using System.Text;
using System.Text.RegularExpressions;
using DailyReview.Core.GitHub;

namespace DailyReview.Core.Review;

public record ContextBuildResult(string Context, int SkippedFilesForSize, int SkippedFilesVendor);

public sealed class DiffContextBuilder
{
    private const int MaxPatchLines = 300;
    private const int MaxContentCharacters = 16_000;
    private static readonly Regex HunkHeaderPattern = new(
        "@@ -\\d+(?:,\\d+)? \\+(\\d+)(?:,(\\d+))? @@",
        RegexOptions.Compiled);

    public Dictionary<string, HashSet<int>> GetValidLinesPerFile(PullRequestInfo prInfo)
    {
        ArgumentNullException.ThrowIfNull(prInfo);

        var validLines = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var file in prInfo.Files)
        {
            if (file.Patch is null)
            {
                continue;
            }

            if (!validLines.TryGetValue(file.FilePath, out var fileLines))
            {
                fileLines = [];
                validLines[file.FilePath] = fileLines;
            }

            foreach (Match hunk in HunkHeaderPattern.Matches(file.Patch))
            {
                var newStart = int.Parse(hunk.Groups[1].Value);
                var newCount = hunk.Groups[2].Success ? int.Parse(hunk.Groups[2].Value) : 1;
                for (var line = newStart; line < newStart + newCount; line++)
                {
                    fileLines.Add(line);
                }
            }
        }

        return validLines;
    }

    public ContextBuildResult BuildContext(PullRequestInfo prInfo)
    {
        ArgumentNullException.ThrowIfNull(prInfo);

        var description = prInfo.Description ?? string.Empty;
        if (description.Length > 300)
        {
            description = description[..300];
        }

        var context = new StringBuilder();
        context.Append("PR Title: ").Append(prInfo.Title)
            .Append('\n')
            .Append("PR Description: ").Append(description)
            .Append("\n\n");

        var reviewableFiles = prInfo.Files.Where(file => !IsGeneratedOrVendorFile(file.FilePath)).ToList();
        var skippedFilesVendor = prInfo.Files.Count - reviewableFiles.Count;
        var includedCharacterCount = 0;
        var includedAnyFile = false;
        var skippedFilesForSize = 0;

        for (var index = 0; index < reviewableFiles.Count; index++)
        {
            var file = reviewableFiles[index];
            var patch = TruncatePatch(file.Patch);
            var fileContent = $"### File: {file.FilePath} (Language: {GetLanguageFromExtension(file.FilePath)})\n{patch}\n\n";

            if (includedCharacterCount + fileContent.Length > MaxContentCharacters)
            {
                skippedFilesForSize = reviewableFiles.Count - index;
                context.Append($"[{skippedFilesForSize} additional files not included due to size limits]");
                break;
            }

            context.Append(fileContent);
            includedCharacterCount += fileContent.Length;
            includedAnyFile = true;
        }

        if (!includedAnyFile && reviewableFiles.Count == 0)
        {
            context.Append("No reviewable file changes found.");
        }

        return new ContextBuildResult(context.ToString(), skippedFilesForSize, skippedFilesVendor);
    }

    private static bool IsGeneratedOrVendorFile(string filePath)
    {
        var normalizedPath = filePath.Replace('\\', '/');
        return normalizedPath.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("/dist/", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("/.git/", StringComparison.OrdinalIgnoreCase);
    }

    private static string TruncatePatch(string patch)
    {
        var normalizedPatch = patch.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedPatch.Split('\n');
        var lineCount = normalizedPatch.EndsWith('\n') ? lines.Length - 1 : lines.Length;

        if (lineCount <= MaxPatchLines)
        {
            return normalizedPatch;
        }

        return string.Join('\n', lines.Take(MaxPatchLines)) +
               $"\n[diff truncated, {lineCount - MaxPatchLines} lines omitted]";
    }

    private static string GetLanguageFromExtension(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".js" or ".jsx" => "JavaScript",
            ".ts" or ".tsx" => "TypeScript",
            ".py" => "Python",
            ".java" => "Java",
            ".go" => "Go",
            ".rb" => "Ruby",
            ".php" => "PHP",
            ".cpp" or ".cc" or ".h" or ".hpp" => "C++",
            ".rs" => "Rust",
            ".kt" => "Kotlin",
            ".swift" => "Swift",
            _ => "Unknown"
        };
}
