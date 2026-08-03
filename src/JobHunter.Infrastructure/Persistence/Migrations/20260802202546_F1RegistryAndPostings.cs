using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F1RegistryAndPostings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    canonical_domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    careers_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    hq_country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    employee_band = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ats_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ats_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    board_token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(3,2)", nullable: false),
                    evidence = table.Column<string>(type: "jsonb", nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ats_bindings", x => x.id);
                    table.ForeignKey(
                        name: "FK_ats_bindings_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    binding_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    requests_per_second = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    consecutive_failures = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    quarantined_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_sources", x => x.id);
                    table.ForeignKey(
                        name: "FK_job_sources_ats_bindings_binding_id",
                        column: x => x.binding_id,
                        principalTable: "ats_bindings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_job_sources_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "raw_postings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    content_hash = table.Column<string>(type: "char(64)", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    http_status = table.Column<short>(type: "smallint", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_raw_postings", x => x.id);
                    table.ForeignKey(
                        name: "FK_raw_postings_job_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "job_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "source_fetch_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    http_status = table.Column<short>(type: "smallint", nullable: false),
                    postings_returned = table.Column<int>(type: "integer", nullable: false),
                    postings_changed = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    detail = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_fetch_log", x => x.id);
                    table.ForeignKey(
                        name: "FK_source_fetch_log_job_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "job_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_ats_bindings_live",
                table: "ats_bindings",
                columns: new[] { "company_id", "ats_kind", "board_token" },
                unique: true,
                filter: "retired_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "idx_companies_active",
                table: "companies",
                column: "is_active",
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "uq_companies_domain",
                table: "companies",
                column: "canonical_domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_job_sources_dispatch",
                table: "job_sources",
                columns: new[] { "quarantined_until", "last_fetched_at" },
                filter: "quarantined_until IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_job_sources_binding_id",
                table: "job_sources",
                column: "binding_id");

            migrationBuilder.CreateIndex(
                name: "IX_job_sources_company_id",
                table: "job_sources",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "idx_raw_postings_fetched",
                table: "raw_postings",
                column: "fetched_at");

            migrationBuilder.CreateIndex(
                name: "idx_raw_postings_source_seen",
                table: "raw_postings",
                columns: new[] { "source_id", "last_seen_at" });

            migrationBuilder.CreateIndex(
                name: "uq_raw_postings_dedup",
                table: "raw_postings",
                columns: new[] { "source_id", "external_id", "content_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_fetch_log_source_started",
                table: "source_fetch_log",
                columns: new[] { "source_id", "started_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "raw_postings");

            migrationBuilder.DropTable(
                name: "source_fetch_log");

            migrationBuilder.DropTable(
                name: "job_sources");

            migrationBuilder.DropTable(
                name: "ats_bindings");

            migrationBuilder.DropTable(
                name: "companies");
        }
    }
}
