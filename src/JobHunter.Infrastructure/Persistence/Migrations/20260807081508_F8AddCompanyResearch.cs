using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F8AddCompanyResearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_research",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    claims_discarded = table.Column<int>(type: "integer", nullable: false),
                    prompt_version = table.Column<string>(type: "text", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    categories_unavailable = table.Column<string>(type: "jsonb", nullable: false),
                    categories_covered = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_research", x => x.id);
                    table.ForeignKey(
                        name: "FK_company_research_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_company_research_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "research_claims",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    claim = table.Column<string>(type: "text", nullable: false),
                    is_warning = table.Column<bool>(type: "boolean", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    research_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_claims", x => x.id);
                    table.ForeignKey(
                        name: "FK_research_claims_company_research_research_id",
                        column: x => x.research_id,
                        principalTable: "company_research",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "research_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    text_length = table.Column<int>(type: "integer", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    research_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_sources", x => x.id);
                    table.UniqueConstraint("AK_research_sources_research_id_id", x => new { x.research_id, x.id });
                    table.ForeignKey(
                        name: "FK_research_sources_company_research_research_id",
                        column: x => x.research_id,
                        principalTable: "company_research",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_research_company_latest",
                table: "company_research",
                columns: new[] { "company_id", "generated_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_company_research_run_id",
                table: "company_research",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "uq_research_company_run",
                table: "company_research",
                columns: new[] { "company_id", "run_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_claims_research",
                table: "research_claims",
                columns: new[] { "research_id", "category" });

            migrationBuilder.CreateIndex(
                name: "idx_claims_warnings",
                table: "research_claims",
                column: "research_id",
                filter: "is_warning");

            migrationBuilder.CreateIndex(
                name: "idx_sources_research",
                table: "research_sources",
                columns: new[] { "research_id", "category" });

            migrationBuilder.CreateIndex(
                name: "uq_sources_url",
                table: "research_sources",
                columns: new[] { "research_id", "url" },
                unique: true);

            // Invariant 5 in the schema: a claim may only cite a source in its own dossier. EF has no
            // navigation to model a composite foreign key, so it is declared here in raw SQL —
            // research_claims(research_id, source_id) references research_sources(research_id, id), the
            // alternate key. It is DEFERRABLE INITIALLY DEFERRED so EF, which does not know claims depend on
            // sources through this key, may insert the two in any order within one SaveChanges transaction;
            // the check fires at commit. A claim citing a source from another dossier — a different research_id
            // — is a foreign-key violation, so an uncited claim is unrepresentable rather than merely rejected.
            migrationBuilder.Sql(
                """
                ALTER TABLE research_claims
                ADD CONSTRAINT fk_research_claims_source
                FOREIGN KEY (research_id, source_id)
                REFERENCES research_sources (research_id, id)
                ON DELETE CASCADE
                DEFERRABLE INITIALLY DEFERRED;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "research_claims");

            migrationBuilder.DropTable(
                name: "research_sources");

            migrationBuilder.DropTable(
                name: "company_research");
        }
    }
}
