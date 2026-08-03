using System.Text.Json;
using System.Text.Json.Nodes;
using JobHunter.Domain.Search;

namespace JobHunter.Search;

/// <summary>
/// The one place a <see cref="JobDocument"/> or the <see cref="SearchSchema"/> becomes Typesense wire
/// JSON, and back (F9-T02). The mapping is written by hand — field by field, never by reflection over the
/// record — so the serialised shape is a second explicit statement of the allowlist (QG-2): a new
/// property on <see cref="JobDocument"/> is not serialised until someone adds it here, and an optional
/// field that is null is <em>omitted</em> rather than sent as JSON null, which is how Typesense marks a
/// document without that value.
/// </summary>
internal static class TypesenseSerialization
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>The collection-creation body: name, fields, default sort and token separators.</summary>
    public static string SchemaJson(string collectionName)
    {
        var fields = new JsonArray();
        foreach (var field in SearchSchema.Fields)
        {
            var node = new JsonObject
            {
                ["name"] = field.Name,
                ["type"] = field.Type,
            };
            if (field.Facet)
            {
                node["facet"] = true;
            }

            if (field.Sort)
            {
                node["sort"] = true;
            }

            if (field.Optional)
            {
                node["optional"] = true;
            }

            fields.Add(node);
        }

        var separators = new JsonArray();
        foreach (var separator in SearchSchema.TokenSeparators)
        {
            separators.Add(separator);
        }

        var schema = new JsonObject
        {
            ["name"] = collectionName,
            ["fields"] = fields,
            ["default_sorting_field"] = SearchSchema.DefaultSortingField,
            ["token_separators"] = separators,
        };

        return schema.ToJsonString(Options);
    }

    /// <summary>One document as a JSON object, with optional-null fields omitted (never sent as null).</summary>
    public static string DocumentJson(JobDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return DocumentNode(document).ToJsonString(Options);
    }

    /// <summary>A batch of documents as newline-delimited JSON, the Typesense import body format.</summary>
    public static string DocumentsJsonl(IReadOnlyList<JobDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return string.Join('\n', documents.Select(d => DocumentNode(d).ToJsonString(Options)));
    }

    private static JsonObject DocumentNode(JobDocument d)
    {
        // Required fields — always present.
        var node = new JsonObject
        {
            ["id"] = d.Id,
            ["title"] = d.Title,
            ["companyName"] = d.CompanyName,
            ["companyDomain"] = d.CompanyDomain,
            ["description"] = d.Description,
            ["technologies"] = StringArray(d.Technologies),
            ["countries"] = StringArray(d.Countries),
            ["remotePolicy"] = d.RemotePolicy,
            ["employmentType"] = d.EmploymentType,
            ["score"] = d.Score,
            ["firstSeenAt"] = d.FirstSeenAt,
            ["status"] = d.Status,
        };

        // Optional fields — present only when the projection carried a value (Typesense "optional").
        if (d.Seniority is not null)
        {
            node["seniority"] = d.Seniority;
        }

        if (d.CompanyStage is not null)
        {
            node["companyStage"] = d.CompanyStage;
        }

        if (d.AiUsage is not null)
        {
            node["aiUsage"] = d.AiUsage;
        }

        if (d.SalaryMin is not null)
        {
            node["salaryMin"] = d.SalaryMin.Value;
        }

        if (d.SalaryMax is not null)
        {
            node["salaryMax"] = d.SalaryMax.Value;
        }

        if (d.SalaryCurrency is not null)
        {
            node["salaryCurrency"] = d.SalaryCurrency;
        }

        if (d.PostedAt is not null)
        {
            node["postedAt"] = d.PostedAt.Value;
        }

        if (d.ApplicationStatus is not null)
        {
            node["applicationStatus"] = d.ApplicationStatus;
        }

        return node;
    }

    private static JsonArray StringArray(IReadOnlyList<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }
}
