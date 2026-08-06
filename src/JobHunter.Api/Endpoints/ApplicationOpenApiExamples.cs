using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace JobHunter.Api.Endpoints;

/// <summary>
/// The OpenAPI examples for the F6 application-tracking endpoints (T09 done-when 5). A documented endpoint without
/// an example is still a guessing game, so each of the five operations is given a concrete example: the three reads
/// carry a response body a client can read the shape from, and the two writes carry a request body the Owner can
/// copy and adapt. Registered as an <see cref="IOpenApiOperationTransformer"/> so the examples live beside the
/// contracts they illustrate rather than being scattered across <c>.WithOpenApi</c> call sites, and matched by the
/// operation's route and method so a renamed route surfaces here rather than silently losing its example.
///
/// <para>No example carries a CV-derived value or a match reason — the CV crosses exactly one boundary, and it is
/// not this one (QG-2). The timestamps are Unix seconds, the same wire form the responses use.</para>
/// </summary>
internal sealed partial class ApplicationOpenApiExamples : IOpenApiOperationTransformer
{
    [GeneratedRegex(@"\{([^:}]+)(:[^}]+)?\}")]
    private static partial Regex RouteConstraint();

    // A fixed illustrative instant (2026-01-15T09:00:00Z) and its neighbours, so the example reads coherently.
    private const long AppliedAt = 1_768_467_600;   // applied a few days before the activity below
    private const long LastActivityAt = 1_768_726_800;
    private const long NextActionAt = 1_768_986_000;

    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        // The relative path carries route constraints (e.g. {id:guid}); the OpenAPI paths are keyed by the
        // bare parameter name, so strip the constraint before matching so a route stays matched here.
        var route = "/" + RouteConstraint().Replace(context.Description.RelativePath ?? string.Empty, "{$1}").TrimStart('/');
        var method = context.Description.HttpMethod?.ToUpperInvariant();

        switch ((method, route))
        {
            case ("GET", "/api/applications"):
                SetResponseExample(operation, PipelineExample());
                break;
            case ("GET", "/api/applications/due"):
                SetResponseExample(operation, DueExample());
                break;
            case ("GET", "/api/applications/{id}"):
                SetResponseExample(operation, DetailExample());
                break;
            case ("POST", "/api/applications/{id}/status"):
                SetRequestExample(operation, StatusChangeExample());
                break;
            case ("POST", "/api/applications/{id}/notes"):
                SetRequestExample(operation, NoteExample());
                break;
        }

        return Task.CompletedTask;
    }

    // Attaches the example to the request body's JSON media type (the Owner's copy-and-adapt payload).
    private static void SetRequestExample(OpenApiOperation operation, JsonNode example)
    {
        if (operation.RequestBody?.Content is { } content && content.TryGetValue("application/json", out var media))
        {
            media.Example = example;
        }
    }

    // Attaches the example to the first success response's JSON media type (the shape a client can expect).
    private static void SetResponseExample(OpenApiOperation operation, JsonNode example)
    {
        if (operation.Responses is null)
        {
            return;
        }

        foreach (var (status, response) in operation.Responses)
        {
            if (status.StartsWith('2') && response.Content is { } content
                && content.TryGetValue("application/json", out var media))
            {
                media.Example = example;
                return;
            }
        }
    }

    private static JsonObject PipelineEntry() => new JsonObject
    {
        ["id"] = "5b1f6d2e-8c4a-4e1b-9f3a-2c7d6e5f4a3b",
        ["jobId"] = "9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d",
        ["title"] = "Staff Backend Engineer",
        ["company"] = "Snowflake",
        ["score"] = 95m,
        ["postingClosed"] = false,
        ["appliedAt"] = AppliedAt,
        ["lastActivityAt"] = LastActivityAt,
        ["nextActionAt"] = NextActionAt,
        ["daysInStage"] = 5,
    };

    private static JsonObject PipelineExample() => new JsonObject
    {
        ["counts"] = new JsonObject { ["Interview"] = 1 },
        ["groups"] = new JsonArray
        {
            new JsonObject
            {
                ["status"] = "Interview",
                ["applications"] = new JsonArray { PipelineEntry() },
            },
        },
    };

    private static JsonArray DueExample() => new JsonArray
    {
        new JsonObject
        {
            ["applicationId"] = "5b1f6d2e-8c4a-4e1b-9f3a-2c7d6e5f4a3b",
            ["jobId"] = "9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d",
            ["title"] = "Staff Backend Engineer",
            ["company"] = "Snowflake",
            ["applyUrl"] = "https://boards.example/snowflake/staff-backend",
            ["status"] = "Applied",
            ["postingClosed"] = false,
        },
    };

    private static JsonObject DetailExample() => new JsonObject
    {
        ["id"] = "5b1f6d2e-8c4a-4e1b-9f3a-2c7d6e5f4a3b",
        ["jobId"] = "9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d",
        ["title"] = "Staff Backend Engineer",
        ["company"] = "Snowflake",
        ["status"] = "Interview",
        ["postingClosed"] = false,
        ["archived"] = false,
        ["appliedAt"] = AppliedAt,
        ["lastActivityAt"] = LastActivityAt,
        ["nextActionAt"] = NextActionAt,
        ["transitions"] = new JsonArray
        {
            new JsonObject
            {
                ["from"] = null,
                ["to"] = "New",
                ["source"] = "Telegram",
                ["detail"] = null,
                ["occurredAt"] = AppliedAt,
            },
            new JsonObject
            {
                ["from"] = "Applied",
                ["to"] = "Interview",
                ["source"] = "Api",
                ["detail"] = "first call scheduled",
                ["occurredAt"] = LastActivityAt,
            },
        },
        ["notes"] = new JsonArray
        {
            new JsonObject
            {
                ["body"] = "Recruiter confirmed the loop for next week.",
                ["createdAt"] = LastActivityAt,
            },
        },
    };

    private static JsonObject StatusChangeExample() => new JsonObject
    {
        ["toStatus"] = "Interview",
        ["detail"] = "first call scheduled",
    };

    private static JsonObject NoteExample() => new JsonObject
    {
        ["body"] = "Recruiter confirmed the loop for next week.",
    };
}
