namespace JobHunter.ArchitectureTests.Violations;

/// <summary>
/// Rule 9 broken (F5 message contract §Escaping): a formatter composing MarkdownV2 emphasis markup with a
/// dynamic value in a single interpolated string, so the value sits next to <em>active</em> markup that was
/// never escaped. This is exactly the hazard the rule forbids — the value must pass through
/// <c>MarkdownV2Escaper.Escape</c> and the markup be added as an adjacent constant, never interpolated
/// around a raw value. The production scan points at the <c>Formatting</c> tree and excludes nothing; here
/// the same scan is pointed at this file to prove it goes red.
/// </summary>
public static class InterpolatedMessageMarkupViolation
{
    public static string Render(string title) => $"*{title}* is bold";
}
