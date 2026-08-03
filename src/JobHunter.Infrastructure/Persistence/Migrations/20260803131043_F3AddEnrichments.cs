using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F3AddEnrichments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "enrichments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salary_min = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    salary_max = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    salary_currency = table.Column<string>(type: "char(3)", nullable: true),
                    salary_period = table.Column<string>(type: "text", nullable: true),
                    salary_confidence = table.Column<decimal>(type: "numeric(3,2)", nullable: true),
                    is_remote = table.Column<bool>(type: "boolean", nullable: false),
                    is_contractor_friendly = table.Column<bool>(type: "boolean", nullable: false),
                    timezone_band = table.Column<string>(type: "text", nullable: false),
                    ai_usage = table.Column<string>(type: "text", nullable: false),
                    company_stage = table.Column<string>(type: "text", nullable: false),
                    prompt_version = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reasons = table.Column<string>(type: "jsonb", nullable: false),
                    technologies = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_enrichments", x => x.id);
                    table.ForeignKey(
                        name: "FK_enrichments_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_enrichments_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_enrichments_job_latest",
                table: "enrichments",
                columns: new[] { "job_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_enrichments_run_id",
                table: "enrichments",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "uq_enrichments_job_run",
                table: "enrichments",
                columns: new[] { "job_id", "run_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "enrichments");
        }
    }
}
