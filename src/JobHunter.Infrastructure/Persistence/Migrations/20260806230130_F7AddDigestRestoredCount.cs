using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F7AddDigestRestoredCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "restored_count",
                table: "digests",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "restored_count",
                table: "digests");
        }
    }
}
