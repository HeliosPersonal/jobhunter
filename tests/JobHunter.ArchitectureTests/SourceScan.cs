using System.Text.RegularExpressions;

namespace JobHunter.ArchitectureTests;

/// <summary>
/// A source-text assertion helper for rules that are about what the code <em>says</em>, not just its
/// type graph — e.g. "nothing but <c>SystemClock</c> reads the ambient clock" (architecture rule 5).
/// It scans the production <c>src/</c> tree for a regex, letting a test exclude the one type that is
/// legitimately allowed to match. Generated migrations and <c>obj/bin</c> output are never scanned.
/// </summary>
public sealed class SourceScan
{
    private readonly Regex _pattern;
    private readonly HashSet<string> _excludedFiles = new(StringComparer.OrdinalIgnoreCase);
    private string? _root;

    private SourceScan(Regex pattern) => _pattern = pattern;

    /// <summary>
    /// Scans <paramref name="absoluteRoot"/> instead of the production <c>src/</c> tree. Used by
    /// <see cref="ViolationFixturesTests"/> to prove the source-level rules go red against the
    /// deliberately-violating fixtures under <c>tests/.../Violations</c>.
    /// </summary>
    public SourceScan InDirectory(string absoluteRoot)
    {
        _root = absoluteRoot;
        return this;
    }

    /// <summary>Every matching <c>file:line</c>, after exclusions.</summary>
    public IReadOnlyList<string> Matches => Scan();

    public static SourceScan ForPattern(string pattern) =>
        new(new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant));

    /// <summary>Excludes the source file that declares <typeparamref name="T"/> from the scan.</summary>
    public SourceScan ExcludingType<T>()
    {
        _excludedFiles.Add($"{typeof(T).Name}.cs");
        return this;
    }

    /// <summary>Excludes any file whose name matches one of <paramref name="fileNames"/>.</summary>
    public SourceScan ExcludingFiles(params string[] fileNames)
    {
        foreach (var name in fileNames)
        {
            _excludedFiles.Add(name);
        }

        return this;
    }

    private List<string> Scan()
    {
        var srcRoot = _root ?? LocateSourceRoot();
        var hits = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsExcluded(file))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                // The rule is about code, not prose: a comment that merely names the banned API (e.g.
                // the doc comment on IClock explaining why DateTime.UtcNow is forbidden) is not a use.
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith('*'))
                {
                    continue;
                }

                if (_pattern.IsMatch(lines[i]))
                {
                    hits.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        return hits;
    }

    private bool IsExcluded(string file)
    {
        var normalized = file.Replace('\\', '/');

        // Never scan build output or generated migrations.
        if (normalized.Contains("/obj/", StringComparison.Ordinal)
            || normalized.Contains("/bin/", StringComparison.Ordinal)
            || normalized.Contains("/Migrations/", StringComparison.Ordinal))
        {
            return true;
        }

        return _excludedFiles.Contains(Path.GetFileName(file));
    }

    private static string LocateSourceRoot()
    {
        // Walk up from the test binary until the solution file is found, then descend into src/.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "JobHunter.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate the repository root (JobHunter.slnx).");
        }

        return Path.Combine(dir.FullName, "src");
    }
}
