using JobHunter.Domain.Reporting;
using JobHunter.Telegram.Callbacks;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace JobHunter.Telegram.Tests.Callbacks;

/// <summary>
/// The signed short id that survives Telegram's 64-byte <c>callback_data</c> limit
/// ([[../contracts/telegram-messages|contract]] §Callback payloads). The load-bearing rules: a card key
/// encodes to an 11-character base64url short id (<c>HMAC-SHA256(cardKey, botSecret)[0..8]</c>); the short
/// id resolves back to its card only among candidates known to belong to a real digest; and a short id
/// signed with a different secret does not resolve — the HMAC is what stops a callback being forged by
/// guessing a card key. The bot secret is config and never appears in a log or an exception (invariant 12).
/// </summary>
public sealed class CallbackDataCodecTests
{
    private const string Secret = "botfather-token-abc123";

    private static CallbackDataCodec Build(string secret = Secret) =>
        new(Options.Create(new TelegramOptions { BotToken = secret, AllowedChatIds = [4242] }));

    private static CardKey KeyFor(int seed) =>
        CardKey.For(new Guid($"11111111-1111-1111-1111-{seed:D12}"), new Guid($"22222222-2222-2222-2222-{seed:D12}"));

    [Fact]
    public void A_card_key_encodes_to_an_eleven_character_short_id()
    {
        var codec = Build();

        var shortId = codec.Encode(KeyFor(1));

        shortId.Length.ShouldBe(11);
    }

    [Fact]
    public void Encoding_is_deterministic_for_a_given_key_and_secret()
    {
        var codec = Build();
        var key = KeyFor(7);

        codec.Encode(key).ShouldBe(codec.Encode(key));
    }

    [Fact]
    public void A_short_id_resolves_back_to_its_card_among_the_candidates()
    {
        var codec = Build();
        var target = KeyFor(3);
        var candidates = new[] { KeyFor(1), KeyFor(2), target, KeyFor(4) };

        var resolved = codec.Resolve(codec.Encode(target), candidates);

        resolved.ShouldBe(target);
    }

    [Fact]
    public void A_short_id_for_a_key_not_among_the_candidates_does_not_resolve()
    {
        var codec = Build();
        var absent = codec.Encode(KeyFor(99));
        var candidates = new[] { KeyFor(1), KeyFor(2), KeyFor(3) };

        codec.Resolve(absent, candidates).ShouldBeNull();
    }

    [Fact]
    public void A_short_id_signed_with_a_different_secret_does_not_resolve()
    {
        var target = KeyFor(5);
        var forged = Build("a-different-secret").Encode(target);

        Build().Resolve(forged, [target]).ShouldBeNull();
    }

    [Fact]
    public void A_forged_or_unparseable_short_id_does_not_resolve_and_does_not_throw()
    {
        var codec = Build();
        var candidates = new[] { KeyFor(1), KeyFor(2) };

        codec.Resolve("not-a-real-id", candidates).ShouldBeNull();
        codec.Resolve(string.Empty, candidates).ShouldBeNull();
    }

    [Fact]
    public void A_null_options_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => new CallbackDataCodec(null!));
    }

    // The weekly rating tap (F4 T20) carries its own signed job id, so it resolves from the payload alone —
    // no candidate lookup, no time window. The signature stops a rating being forged for a job never prompted.

    private static readonly Guid RatedJob = new("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void A_rating_payload_resolves_back_to_its_job_id()
    {
        var codec = Build();

        var resolved = codec.ResolveRating(codec.EncodeRating(RatedJob));

        resolved.ShouldBe(RatedJob);
    }

    [Fact]
    public void Rating_encoding_is_deterministic_for_a_given_job_and_secret()
    {
        var codec = Build();

        codec.EncodeRating(RatedJob).ShouldBe(codec.EncodeRating(RatedJob));
    }

    [Fact]
    public void A_rating_payload_stays_within_the_callback_data_budget()
    {
        // "rat:" + payload must leave headroom under Telegram's 64-byte callback_data limit.
        var codec = Build();

        codec.EncodeRating(RatedJob).Length.ShouldBeLessThan(60);
    }

    [Fact]
    public void A_rating_payload_signed_with_a_different_secret_does_not_resolve()
    {
        var forged = Build("a-different-secret").EncodeRating(RatedJob);

        Build().ResolveRating(forged).ShouldBeNull();
    }

    [Fact]
    public void A_tampered_rating_payload_does_not_resolve()
    {
        var codec = Build();
        var payload = codec.EncodeRating(RatedJob);

        // Flip the first character; the truncated signature no longer matches the job id it guards.
        var tampered = (payload[0] == 'A' ? 'B' : 'A') + payload[1..];

        codec.ResolveRating(tampered).ShouldBeNull();
    }

    [Fact]
    public void A_forged_or_unparseable_rating_payload_does_not_resolve_and_does_not_throw()
    {
        var codec = Build();

        codec.ResolveRating("not-a-real-payload").ShouldBeNull();
        codec.ResolveRating(string.Empty).ShouldBeNull();
        codec.ResolveRating(null).ShouldBeNull();
    }
}
