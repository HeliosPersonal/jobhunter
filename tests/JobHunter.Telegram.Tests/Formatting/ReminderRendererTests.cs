using JobHunter.Domain.Applications;
using JobHunter.Telegram.Formatting;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Formatting;

/// <summary>
/// T06: the reminder renderer turns one due application into the nudge the sweep sends. It names the
/// application and a <em>suggested action</em>, never just a fact (done-when 2): a still-open stale
/// application gets a "chase this" nudge with an open-posting button; a closed posting gets a "drop it or
/// apply elsewhere" nudge and no open button, because the posting is gone. Every dynamic value passes
/// through the one MarkdownV2 escaper, so a hostile title cannot break the send.
/// </summary>
public sealed class ReminderRendererTests
{
    private readonly ReminderRenderer _renderer = new();

    private static DueReminder Reminder(
        ApplicationStatus status, bool postingClosed, string title = "Staff SRE", string company = "Acme") =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), title, company, "https://acme.test/apply",
            status, postingClosed, LastReminderCondition: null);

    [Fact]
    public void A_stale_open_application_names_the_role_and_a_chase_action_with_an_open_button()
    {
        var message = _renderer.Render(Reminder(ApplicationStatus.Applied, postingClosed: false));

        message.Text.ShouldContain("Staff SRE");
        message.Text.ShouldContain("Acme");
        // A suggested action, not just a fact: it says how long it has been waiting and what to do next.
        message.Text.ShouldContain("Applied");
        message.HasKeyboard.ShouldBeTrue();
        var button = message.Keyboard[0][0];
        button.Url.ShouldBe("https://acme.test/apply");
    }

    [Fact]
    public void A_closed_posting_suggests_dropping_or_applying_elsewhere_and_shows_no_open_button()
    {
        var message = _renderer.Render(Reminder(ApplicationStatus.Saved, postingClosed: true));

        message.Text.ShouldContain("closed");
        message.HasKeyboard.ShouldBeFalse();
    }

    [Fact]
    public void A_hostile_title_is_escaped_so_it_cannot_break_the_send()
    {
        var message = _renderer.Render(Reminder(ApplicationStatus.Applied, postingClosed: false, title: "C++ (Staff)"));

        // The parentheses and plus signs are MarkdownV2 specials and must be backslash-escaped.
        message.Text.ShouldContain(@"C\+\+ \(Staff\)");
    }
}
