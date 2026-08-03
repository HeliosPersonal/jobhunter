using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F3AddEnrichmentAiSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ai_builds_infra",
                table: "enrichments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ai_builds_product",
                table: "enrichments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ai_is_research",
                table: "enrichments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ai_uses_tooling",
                table: "enrichments",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_builds_infra",
                table: "enrichments");

            migrationBuilder.DropColumn(
                name: "ai_builds_product",
                table: "enrichments");

            migrationBuilder.DropColumn(
                name: "ai_is_research",
                table: "enrichments");

            migrationBuilder.DropColumn(
                name: "ai_uses_tooling",
                table: "enrichments");
        }
    }
}
