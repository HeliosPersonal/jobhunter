using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F4AddProfileCareerGoal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "desired_ai_usage_floor",
                table: "profiles",
                type: "text",
                nullable: true);

            // Existing Owner rows must satisfy the NOT NULL and deserialize as an empty list, so the default
            // is a valid empty JSON array — never "" (invalid jsonb, and StringListJson/EnumListJson expect
            // an array). New rows carry the aggregate's own serialized value, not this default.
            migrationBuilder.AddColumn<string>(
                name: "target_role_families",
                table: "profiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "target_titles",
                table: "profiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "desired_ai_usage_floor",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "target_role_families",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "target_titles",
                table: "profiles");
        }
    }
}
