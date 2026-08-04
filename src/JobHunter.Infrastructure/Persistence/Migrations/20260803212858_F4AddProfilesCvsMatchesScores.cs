using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F4AddProfilesCvsMatchesScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    salary_floor = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    salary_floor_currency = table.Column<string>(type: "char(3)", nullable: true),
                    timezone_band = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    employment_types = table.Column<string>(type: "jsonb", nullable: false),
                    preferred_countries = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scores",
                columns: table => new
                {
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    final_score = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    match_component = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    preference_component = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    freshness_component = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    confidence_multiplier = table.Column<decimal>(type: "numeric(3,2)", nullable: false),
                    preference_model_id = table.Column<Guid>(type: "uuid", nullable: true),
                    suppressed = table.Column<bool>(type: "boolean", nullable: false),
                    suppression_reason = table.Column<string>(type: "text", nullable: true),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scores", x => new { x.job_id, x.run_id });
                    table.ForeignKey(
                        name: "FK_scores_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_scores_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cv_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<short>(type: "smallint", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    media_type = table.Column<string>(type: "text", nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "char(64)", nullable: false),
                    extracted_text = table.Column<string>(type: "text", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cv_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_cv_versions_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cv_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_score = table.Column<short>(type: "smallint", nullable: false),
                    interview_probability = table.Column<string>(type: "text", nullable: false),
                    salary_expectation_min = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    salary_expectation_max = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    salary_expectation_currency = table.Column<string>(type: "char(3)", nullable: true),
                    prompt_version = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    missing_skills = table.Column<string>(type: "jsonb", nullable: false),
                    reasons = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matches", x => x.id);
                    table.ForeignKey(
                        name: "FK_matches_cv_versions_cv_version_id",
                        column: x => x.cv_version_id,
                        principalTable: "cv_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_matches_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_matches_profiles_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_matches_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_cv_versions_hash",
                table: "cv_versions",
                columns: new[] { "profile_id", "content_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_matches_current",
                table: "matches",
                columns: new[] { "job_id", "created_at" },
                descending: new[] { false, true },
                filter: "is_current");

            migrationBuilder.CreateIndex(
                name: "idx_matches_cv_version",
                table: "matches",
                column: "cv_version_id",
                filter: "is_current");

            migrationBuilder.CreateIndex(
                name: "IX_matches_profile_id",
                table: "matches",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "IX_matches_run_id",
                table: "matches",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "uq_matches_job_run_profile",
                table: "matches",
                columns: new[] { "job_id", "run_id", "profile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_scores_run_final",
                table: "scores",
                columns: new[] { "run_id", "final_score" },
                descending: new[] { false, true },
                filter: "NOT suppressed");

            migrationBuilder.CreateIndex(
                name: "idx_scores_suppressed",
                table: "scores",
                column: "run_id",
                filter: "suppressed");

            // The two partial unique indexes that carry F4's "exactly one" rules (data-model §Indexes).
            // EF cannot model a partial unique index, so both are declared here in raw SQL and named for
            // documentation in ProfileConfiguration and CvVersionConfiguration.
            //
            // uq_profiles_active — unique on `is_active` filtered to the active rows, so at most one
            // Profile can be active at a time. A second active Profile fails loudly at commit.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX uq_profiles_active ON profiles (is_active) WHERE is_active;");

            // uq_cv_versions_active — unique on `profile_id` filtered to the active rows, so at most one
            // CV version per profile can be active. A second active version for a profile fails at commit.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX uq_cv_versions_active ON cv_versions (profile_id) WHERE is_active;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS uq_cv_versions_active;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS uq_profiles_active;");

            migrationBuilder.DropTable(
                name: "matches");

            migrationBuilder.DropTable(
                name: "scores");

            migrationBuilder.DropTable(
                name: "cv_versions");

            migrationBuilder.DropTable(
                name: "profiles");
        }
    }
}
