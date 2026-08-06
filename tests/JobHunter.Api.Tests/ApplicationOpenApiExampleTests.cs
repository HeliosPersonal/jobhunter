using System.Text.Json;
using Shouldly;
using Xunit;

namespace JobHunter.Api.Tests;

/// <summary>
/// The OpenAPI-examples contract for the F6 application-tracking endpoints (T09 done-when 5): every one of the
/// five endpoints appears in the generated document <em>with an example</em> — the two writes carry a request-body
/// example the Owner can copy, and the three reads carry a response example a client can read the shape from. A
/// documented endpoint without an example is still a guessing game; this turns "add an example" from a nicety into
/// a build assertion. No example carries a CV-derived value or a match reason (the CV crosses exactly one boundary).
/// </summary>
public sealed class ApplicationOpenApiExampleTests : IClassFixture<EndpointsHostFactory>
{
    private readonly EndpointsHostFactory _factory;

    public ApplicationOpenApiExampleTests(EndpointsHostFactory factory) => _factory = factory;

    private async Task<JsonElement> PathsAsync()
    {
        using var client = _factory.OwnerClient();
        var raw = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        // Parse into a document owned by the caller's scope; clone the paths so it survives disposal.
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.GetProperty("paths").Clone();
    }

    /// <summary>True when a media-type map (a <c>content</c> object) carries an inline example on any media type.</summary>
    private static bool HasContentExample(JsonElement content)
    {
        foreach (var mediaType in content.EnumerateObject())
        {
            if (mediaType.Value.TryGetProperty("example", out _) || mediaType.Value.TryGetProperty("examples", out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RequestBodyHasExample(JsonElement operation) =>
        operation.TryGetProperty("requestBody", out var body)
        && body.TryGetProperty("content", out var content)
        && HasContentExample(content);

    private static bool AnyResponseHasExample(JsonElement operation)
    {
        if (!operation.TryGetProperty("responses", out var responses))
        {
            return false;
        }

        foreach (var response in responses.EnumerateObject())
        {
            if (response.Value.TryGetProperty("content", out var content) && HasContentExample(content))
            {
                return true;
            }
        }

        return false;
    }

    private static JsonElement Operation(JsonElement paths, string route, string method)
    {
        paths.TryGetProperty(route, out var pathItem).ShouldBeTrue($"OpenAPI is missing a path for {route}");
        pathItem.TryGetProperty(method, out var operation).ShouldBeTrue($"{route} has no {method} operation");
        return operation;
    }

    [Fact]
    public async Task The_pipeline_read_documents_a_response_example()
    {
        var paths = await PathsAsync();
        AnyResponseHasExample(Operation(paths, "/api/applications", "get"))
            .ShouldBeTrue("GET /api/applications should document a response example");
    }

    [Fact]
    public async Task The_due_read_documents_a_response_example()
    {
        var paths = await PathsAsync();
        AnyResponseHasExample(Operation(paths, "/api/applications/due", "get"))
            .ShouldBeTrue("GET /api/applications/due should document a response example");
    }

    [Fact]
    public async Task The_detail_read_documents_a_response_example()
    {
        var paths = await PathsAsync();
        AnyResponseHasExample(Operation(paths, "/api/applications/{id}", "get"))
            .ShouldBeTrue("GET /api/applications/{id} should document a response example");
    }

    [Fact]
    public async Task The_status_change_write_documents_a_request_body_example()
    {
        var paths = await PathsAsync();
        RequestBodyHasExample(Operation(paths, "/api/applications/{id}/status", "post"))
            .ShouldBeTrue("POST /api/applications/{id}/status should document a request-body example");
    }

    [Fact]
    public async Task The_note_write_documents_a_request_body_example()
    {
        var paths = await PathsAsync();
        RequestBodyHasExample(Operation(paths, "/api/applications/{id}/notes", "post"))
            .ShouldBeTrue("POST /api/applications/{id}/notes should document a request-body example");
    }
}
