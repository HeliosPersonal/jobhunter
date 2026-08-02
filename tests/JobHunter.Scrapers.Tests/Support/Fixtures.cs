using System.Reflection;

namespace JobHunter.Scrapers.Tests.Support;

/// <summary>
/// Loads a recorded provider payload from the committed fixture corpus (test-plan §Fixture corpus). The
/// files are copied next to the test assembly by the csproj, so a test never touches the network or the
/// source tree at run time.
/// </summary>
internal static class Fixtures
{
    private static readonly string Root = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "Fixtures");

    public static string Load(string provider, string name) =>
        File.ReadAllText(Path.Combine(Root, provider, name));
}
