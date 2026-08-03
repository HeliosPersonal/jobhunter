using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F2JobsAndAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The near-duplicate grouping index (AC-10) is a GIN trigram index, which needs pg_trgm.
            // Created idempotently so the migration is safe on a database where the extension is present.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.CreateTable(
                name: "jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin_raw_posting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fingerprint = table.Column<string>(type: "char(64)", nullable: false),
                    fingerprint_version = table.Column<short>(type: "smallint", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    normalised_title = table.Column<string>(type: "text", nullable: false),
                    seniority = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    apply_url = table.Column<string>(type: "text", nullable: false),
                    locations = table.Column<string>(type: "jsonb", nullable: false),
                    remote_policy = table.Column<string>(type: "text", nullable: false),
                    employment_type = table.Column<string>(type: "text", nullable: false),
                    salary_min = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    salary_max = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    salary_currency = table.Column<string>(type: "char(3)", nullable: true),
                    salary_period = table.Column<string>(type: "text", nullable: true),
                    salary_raw = table.Column<string>(type: "text", nullable: true),
                    posted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    posted_at_granularity = table.Column<string>(type: "text", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    is_tier2 = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_jobs_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_aliases",
                columns: table => new
                {
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw_posting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_aliases", x => new { x.job_id, x.raw_posting_id });
                    table.ForeignKey(
                        name: "FK_job_aliases_job_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "job_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_job_aliases_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_job_aliases_raw_postings_raw_posting_id",
                        column: x => x.raw_posting_id,
                        principalTable: "raw_postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "job_technologies",
                columns: table => new
                {
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technology = table.Column<string>(type: "text", nullable: false),
                    matched_via = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_technologies", x => new { x.job_id, x.technology });
                    table.ForeignKey(
                        name: "FK_job_technologies_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_job_aliases_raw",
                table: "job_aliases",
                column: "raw_posting_id");

            migrationBuilder.CreateIndex(
                name: "IX_job_aliases_source_id",
                table: "job_aliases",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "idx_job_technologies_tech",
                table: "job_technologies",
                column: "technology");

            migrationBuilder.CreateIndex(
                name: "idx_jobs_company_status",
                table: "jobs",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_jobs_first_seen",
                table: "jobs",
                column: "first_seen_at",
                filter: "status = 'Live'");

            migrationBuilder.CreateIndex(
                name: "idx_jobs_last_seen",
                table: "jobs",
                column: "last_seen_at",
                filter: "status = 'Live'");

            migrationBuilder.CreateIndex(
                name: "uq_jobs_fingerprint",
                table: "jobs",
                column: "fingerprint",
                unique: true);

            // Near-duplicate grouping (AC-10): a GIN trigram index over the normalised title, used by the
            // T06 near-duplicate candidate scan. EF does not model GIN trigram operator classes, so it is
            // declared in raw SQL here.
            migrationBuilder.Sql(
                "CREATE INDEX idx_jobs_normalised_title_trgm ON jobs USING gin (normalised_title gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_aliases");

            migrationBuilder.DropTable(
                name: "job_technologies");

            migrationBuilder.DropTable(
                name: "jobs");
        }
    }
}
