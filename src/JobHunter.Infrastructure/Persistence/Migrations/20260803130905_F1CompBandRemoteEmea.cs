using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the two curated comp-and-remote segmentation columns to <c>companies</c> (T15, TUNE-10):
    /// <c>comp_band</c> (the coarse comp posture, persisted as text — a category, not money) and
    /// <c>remote_emea_friendly</c> (whether the employer hires remote from EMEA / Ukraine). Both are
    /// nullable, so every existing row stays valid and untagged; the fields are advisory and bias discovery
    /// and digest ordering toward the target band rather than filtering.
    /// </summary>
    public partial class F1CompBandRemoteEmea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "comp_band",
                table: "companies",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "remote_emea_friendly",
                table: "companies",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "comp_band",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "remote_emea_friendly",
                table: "companies");
        }
    }
}
