using System.Text.Json.Nodes;
using JobHunter.Api.Endpoints;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Shouldly;
using Xunit;

namespace JobHunter.Api.Tests;

/// <summary>
/// Direct-construction cover for the example transformer's defensive guard arms that the host-driven
/// <see cref="ApplicationOpenApiExampleTests"/> cannot reach, because the fully-generated OpenAPI document always
/// populates a request body and a described 2xx response. Here the transformer is fed hand-built operations that
/// omit them — a matched route whose response set is null, whose only response is a non-2xx, whose 2xx response
/// carries no JSON media type; a matched write route with no request body and with a non-JSON body; and an
/// unmatched (null method, null path) operation — so each half of the null-and-shape guards is exercised with no
/// host and no HTTP. The happy path (a JSON media type receives the example) is asserted so the set arm is proven.
/// </summary>
public sealed class ApplicationOpenApiExamplesBranchTests
{
    private static readonly ApplicationOpenApiExamples Transformer = new();

    private static OpenApiOperationTransformerContext Context(string? httpMethod, string? relativePath) => new()
    {
        DocumentName = "v1",
        Description = new ApiDescription { HttpMethod = httpMethod, RelativePath = relativePath },
        ApplicationServices = null!,
    };

    private static async Task TransformAsync(OpenApiOperation operation, string? method, string? path) =>
        await Transformer.TransformAsync(operation, Context(method, path), CancellationToken.None);

    private static OpenApiMediaType JsonMediaOf(OpenApiOperation operation) =>
        ((OpenApiResponse)operation.Responses!["200"]).Content!["application/json"];

    [Fact]
    public async Task An_unmatched_route_with_a_null_method_and_path_sets_no_example()
    {
        // Null RelativePath drives the `?? string.Empty` arm; null HttpMethod drives the `?.ToUpperInvariant()`
        // arm. The (null, "/") tuple matches no case, so the operation is left untouched.
        var operation = new OpenApiOperation { RequestBody = new OpenApiRequestBody() };

        await TransformAsync(operation, method: null, path: null);

        operation.RequestBody.Content.ShouldBeNull();
    }

    [Fact]
    public async Task A_matched_read_with_a_null_response_set_returns_without_touching_it()
    {
        // Responses is null: the early-return arm of SetResponseExample fires and nothing is dereferenced.
        var operation = new OpenApiOperation { Responses = null };

        await TransformAsync(operation, "GET", "api/applications");

        operation.Responses.ShouldBeNull();
    }

    [Fact]
    public async Task A_matched_read_whose_only_response_is_non_2xx_sets_no_example()
    {
        // A 4xx response is skipped by the StartsWith('2') guard, so no example is attached.
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["400"] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() },
                },
            },
        };

        await TransformAsync(operation, "GET", "api/applications/due");

        ((OpenApiResponse)operation.Responses["400"]).Content!["application/json"].Example.ShouldBeNull();
    }

    [Fact]
    public async Task A_matched_read_whose_2xx_response_has_no_json_media_sets_no_example()
    {
        // The status is 2xx but the response carries only a non-JSON media type, so the inner content guard
        // fails and the loop finds nothing to attach the example to.
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType> { ["text/plain"] = new() },
                },
            },
        };

        await TransformAsync(operation, "GET", "api/applications/{id}");

        ((OpenApiResponse)operation.Responses["200"]).Content!["text/plain"].Example.ShouldBeNull();
    }

    [Fact]
    public async Task A_matched_read_whose_2xx_response_has_a_json_media_receives_the_example()
    {
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() },
                },
            },
        };

        await TransformAsync(operation, "GET", "api/applications");

        JsonMediaOf(operation).Example.ShouldBeOfType<JsonObject>();
    }

    [Fact]
    public async Task A_matched_write_with_no_request_body_sets_no_example()
    {
        // RequestBody is null: the null-conditional short-circuits and SetRequestExample attaches nothing.
        var operation = new OpenApiOperation { RequestBody = null };

        await TransformAsync(operation, "POST", "api/applications/{id}/status");

        operation.RequestBody.ShouldBeNull();
    }

    [Fact]
    public async Task A_matched_write_whose_body_has_no_json_media_sets_no_example()
    {
        var operation = new OpenApiOperation
        {
            RequestBody = new OpenApiRequestBody
            {
                Content = new Dictionary<string, OpenApiMediaType> { ["text/plain"] = new() },
            },
        };

        await TransformAsync(operation, "POST", "api/applications/{id}/notes");

        ((OpenApiRequestBody)operation.RequestBody).Content!["text/plain"].Example.ShouldBeNull();
    }

    [Fact]
    public async Task A_matched_write_whose_body_has_a_json_media_receives_the_example()
    {
        var operation = new OpenApiOperation
        {
            RequestBody = new OpenApiRequestBody
            {
                Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() },
            },
        };

        await TransformAsync(operation, "POST", "api/applications/{id}/status");

        ((OpenApiRequestBody)operation.RequestBody).Content!["application/json"].Example.ShouldBeOfType<JsonObject>();
    }
}
