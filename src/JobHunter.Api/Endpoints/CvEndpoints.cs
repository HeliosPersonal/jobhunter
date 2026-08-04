using JobHunter.Application.Profiles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace JobHunter.Api.Endpoints;

/// <summary>
/// The CV endpoint (SAD §6.3, T03): the owner-scoped <c>POST /api/cv</c> the Owner uploads a new CV
/// through. It is the one place a CV enters the system, and it is deliberately narrow — a multipart file
/// in, a version acknowledgement out. The route declares <c>jobhunter:read</c>, which the scope-plus-Owner
/// policy resolves to "the Owner, with a read token" (the personal-data endpoints are owner-scoped, AC-07);
/// a valid token for any other subject is a 403. The media type is sniffed inside the service, never
/// trusted from the upload's declared content type or file name. No CV text ever crosses back over this
/// boundary — the response carries only the version's identity and number (QG-2).
/// </summary>
public static class CvEndpoints
{
    public static IEndpointRouteBuilder MapCvEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/api/cv", HandleUploadAsync)
            .WithName("UploadCv")
            .WithSummary("Uploads a new CV for the active Profile (Owner only); text is extracted and the binary discarded.")
            .DisableAntiforgery()
            .RequireAuthorization(ApiSecurityExtensions.ReadPolicy);

        return app;
    }

    internal static async Task<IResult> HandleUploadAsync(
        IFormFile? file,
        CvUploadService uploads,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uploads);

        if (file is null || file.Length == 0)
        {
            return Results.Problem(
                type: SearchEndpoints.ErrorTypeBase + "cv-empty",
                title: "No CV was uploaded",
                detail: "The request carried no CV file, or the file was empty.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Buffer the upload once so the service can both sniff the leading bytes and hash the whole content;
        // the byte[] goes out of scope with this handler and is never written to disk.
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        var result = await uploads
            .UploadAsync(file.FileName, buffer.ToArray(), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return ToProblem(result.Error.Code, result.Error.Message);
        }

        var value = result.Value;
        var body = new CvVersionResponse(value.CvVersionId, value.Version, value.Created);

        // Identical content is a no-op: the existing version is returned with 200, not a spurious 201.
        return value.Created
            ? Results.Created($"/api/cv/{value.CvVersionId}", body)
            : Results.Ok(body);
    }

    private static IResult ToProblem(string code, string message)
    {
        var (status, type, title) = code switch
        {
            CvUploadService.Errors.TooLarge =>
                (StatusCodes.Status413PayloadTooLarge, "cv-too-large", "The CV is too large"),
            CvUploadService.Errors.NoActiveProfile =>
                (StatusCodes.Status409Conflict, "cv-no-active-profile", "There is no active Profile"),
            _ => (StatusCodes.Status400BadRequest, "cv-rejected", "The CV could not be accepted"),
        };

        return Results.Problem(
            type: SearchEndpoints.ErrorTypeBase + type,
            title: title,
            detail: message,
            statusCode: status);
    }
}
