using JobHunter.Claude.Anthropic;
using JobHunter.Domain.Abstractions;
using Shouldly;
using Xunit;

namespace JobHunter.Claude.Tests.Anthropic;

/// <summary>
/// The pure response parser (SAD §5): it maps saved Anthropic Batches payloads back onto the port types with
/// no HTTP. The load-bearing rule is tolerance — a per-item provider error, a missing field or an unexpected
/// shape is a recorded value, never a throw that fails the whole batch (QG-3) — so these tests walk every
/// non-happy arm: absent envelopes, unknown statuses, non-number counts, an errored/cancelled item and a
/// message with no tool call. Only genuinely non-JSON input is allowed to fault.
/// </summary>
public sealed class AnthropicResponseParserTests
{
    // --- ParseBatchId ---

    [Fact]
    public void ParseBatchId_reads_the_id()
    {
        AnthropicResponseParser.ParseBatchId("""{"id":"msgbatch_123"}""").ShouldBe("msgbatch_123");
    }

    [Fact]
    public void ParseBatchId_throws_when_the_id_is_null()
    {
        Should.Throw<FormatException>(() => AnthropicResponseParser.ParseBatchId("""{"id":null}"""));
    }

    // --- ParseBatchList ---

    [Fact]
    public void ParseBatchList_reads_each_entry_preserving_order()
    {
        const string body =
            """
            {"data":[
              {"id":"b1","created_at":"2026-08-10T06:00:00Z"},
              {"id":"b2","created_at":"2026-08-09T06:00:00Z"}
            ]}
            """;

        var refs = AnthropicResponseParser.ParseBatchList(body);

        refs.Count.ShouldBe(2);
        refs[0].ProviderBatchId.ShouldBe("b1");
        refs[1].ProviderBatchId.ShouldBe("b2");
    }

    [Fact]
    public void ParseBatchList_returns_empty_when_data_is_absent()
    {
        AnthropicResponseParser.ParseBatchList("""{"object":"list"}""").ShouldBeEmpty();
    }

    [Fact]
    public void ParseBatchList_returns_empty_when_data_is_not_an_array()
    {
        AnthropicResponseParser.ParseBatchList("""{"data":{"id":"x"}}""").ShouldBeEmpty();
    }

    [Fact]
    public void ParseBatchList_skips_an_entry_missing_id_or_created_at()
    {
        const string body =
            """
            {"data":[
              {"created_at":"2026-08-10T06:00:00Z"},
              {"id":"b2"},
              {"id":null,"created_at":"2026-08-10T06:00:00Z"},
              {"id":"b4","created_at":"not-a-date"},
              {"id":"b5","created_at":"2026-08-10T06:00:00Z"}
            ]}
            """;

        var refs = AnthropicResponseParser.ParseBatchList(body);

        // Only the fully-formed final entry survives; every malformed entry is skipped, not thrown.
        refs.Count.ShouldBe(1);
        refs[0].ProviderBatchId.ShouldBe("b5");
    }

    // --- ParseStatus ---

    [Theory]
    [InlineData("in_progress", ProviderBatchState.InProgress)]
    [InlineData("ended", ProviderBatchState.Ended)]
    [InlineData("canceling", ProviderBatchState.Cancelled)]
    [InlineData("cancelled", ProviderBatchState.Cancelled)]
    [InlineData("canceled", ProviderBatchState.Cancelled)]
    [InlineData("expired", ProviderBatchState.Expired)]
    [InlineData("something_new", ProviderBatchState.InProgress)]
    public void ParseStatus_maps_every_processing_status(string status, ProviderBatchState expected)
    {
        var result = AnthropicResponseParser.ParseStatus($$"""{"processing_status":"{{status}}"}""");

        result.State.ShouldBe(expected);
    }

    [Fact]
    public void ParseStatus_sums_errored_canceled_and_expired_into_errored()
    {
        const string body =
            """
            {"processing_status":"ended","request_counts":{"succeeded":5,"errored":2,"canceled":1,"expired":3,"processing":4}}
            """;

        var result = AnthropicResponseParser.ParseStatus(body);

        result.Succeeded.ShouldBe(5);
        result.Errored.ShouldBe(6); // 2 + 1 + 3
        result.Processing.ShouldBe(4);
    }

    [Fact]
    public void ParseStatus_defaults_counts_to_zero_when_request_counts_is_absent()
    {
        var result = AnthropicResponseParser.ParseStatus("""{"processing_status":"ended"}""");

        result.Succeeded.ShouldBe(0);
        result.Errored.ShouldBe(0);
        result.Processing.ShouldBe(0);
    }

    [Fact]
    public void ParseStatus_treats_a_non_number_count_as_zero()
    {
        const string body = """{"processing_status":"ended","request_counts":{"succeeded":"5"}}""";

        var result = AnthropicResponseParser.ParseStatus(body);

        result.Succeeded.ShouldBe(0);
    }

    // --- ParseResultLine ---

    [Fact]
    public void ParseResultLine_lifts_the_tool_input_verbatim_on_success()
    {
        const string line =
            """
            {"custom_id":"item-1","result":{"type":"succeeded","message":{
              "usage":{"input_tokens":100,"output_tokens":20,"cache_read_input_tokens":80},
              "content":[{"type":"text","text":"thinking"},{"type":"tool_use","input":{"reasons":["a"]}}]}}}
            """;

        var item = AnthropicResponseParser.ParseResultLine(line);

        item.CustomId.ShouldBe("item-1");
        item.ProviderError.ShouldBeNull();
        item.RawJson.ShouldNotBeNull();
        item.RawJson!.Replace(" ", "").ShouldBe("""{"reasons":["a"]}""");
        item.Usage.InputTokens.ShouldBe(100);
        item.Usage.OutputTokens.ShouldBe(20);
        item.Usage.CacheReadInputTokens.ShouldBe(80);
    }

    [Fact]
    public void ParseResultLine_defaults_custom_id_to_empty_when_absent()
    {
        // No custom_id and no result envelope: the id defaults to empty and the line is a recorded error.
        var item = AnthropicResponseParser.ParseResultLine("""{"object":"result"}""");

        item.CustomId.ShouldBe(string.Empty);
    }

    [Fact]
    public void ParseResultLine_reports_a_missing_result_envelope_as_a_provider_error()
    {
        var item = AnthropicResponseParser.ParseResultLine("""{"custom_id":"x"}""");

        item.RawJson.ShouldBeNull();
        item.ProviderError.ShouldNotBeNull();
        item.Usage.ShouldBe(TokenUsage.Zero);
    }

    [Fact]
    public void ParseResultLine_describes_an_errored_result_with_the_provider_error_type()
    {
        const string line =
            """
            {"custom_id":"x","result":{"type":"errored","error":{"type":"invalid_request"}}}
            """;

        var item = AnthropicResponseParser.ParseResultLine(line);

        item.RawJson.ShouldBeNull();
        item.ProviderError!.ShouldContain("invalid_request");
    }

    [Fact]
    public void ParseResultLine_describes_a_non_success_result_without_an_error_type()
    {
        // An errored result whose error carries no usable type falls back to the plain result-type message.
        var item = AnthropicResponseParser.ParseResultLine(
            """{"custom_id":"x","result":{"type":"expired","error":{"type":""}}}""");

        item.ProviderError!.ShouldContain("expired");
    }

    [Fact]
    public void ParseResultLine_describes_a_non_success_result_with_no_type_at_all()
    {
        var item = AnthropicResponseParser.ParseResultLine("""{"custom_id":"x","result":{}}""");

        item.ProviderError!.ShouldContain("unknown");
    }

    [Fact]
    public void ParseResultLine_reports_a_succeeded_result_with_no_message()
    {
        var item = AnthropicResponseParser.ParseResultLine(
            """{"custom_id":"x","result":{"type":"succeeded"}}""");

        item.RawJson.ShouldBeNull();
        item.ProviderError.ShouldNotBeNull();
    }

    [Fact]
    public void ParseResultLine_reports_a_message_with_no_tool_use_block()
    {
        const string line =
            """
            {"custom_id":"x","result":{"type":"succeeded","message":{"content":[{"type":"text","text":"hi"}]}}}
            """;

        var item = AnthropicResponseParser.ParseResultLine(line);

        item.RawJson.ShouldBeNull();
        item.ProviderError!.ShouldContain("tool_use");
    }

    [Fact]
    public void ParseResultLine_reports_a_message_whose_content_is_not_an_array()
    {
        var item = AnthropicResponseParser.ParseResultLine(
            """{"custom_id":"x","result":{"type":"succeeded","message":{"content":"nope"}}}""");

        item.RawJson.ShouldBeNull();
        item.ProviderError!.ShouldContain("tool_use");
    }

    [Fact]
    public void ParseResultLine_defaults_usage_to_zero_when_absent()
    {
        const string line =
            """
            {"custom_id":"x","result":{"type":"succeeded","message":{
              "content":[{"type":"tool_use","input":{"ok":true}}]}}}
            """;

        var item = AnthropicResponseParser.ParseResultLine(line);

        item.RawJson.ShouldNotBeNull();
        item.Usage.ShouldBe(TokenUsage.Zero);
    }

    [Fact]
    public void ParseResultLine_treats_non_number_token_counts_as_zero()
    {
        const string line =
            """
            {"custom_id":"x","result":{"type":"succeeded","message":{
              "usage":{"input_tokens":"100","output_tokens":null},
              "content":[{"type":"tool_use","input":{"ok":true}}]}}}
            """;

        var item = AnthropicResponseParser.ParseResultLine(line);

        item.Usage.InputTokens.ShouldBe(0);
        item.Usage.OutputTokens.ShouldBe(0);
        item.Usage.CacheReadInputTokens.ShouldBe(0);
    }
}
