using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Establish the schema Hangfire owns (ADR-0004, T05). Hangfire.PostgreSql creates its own
            // tables on first run under this schema; creating it here means the schema exists on a clean
            // database the moment migrations have applied, independent of Hangfire's own bootstrap.
            migrationBuilder.EnsureSchema(name: "hangfire");

            migrationBuilder.CreateTable(
                name: "platform_markers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_markers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "uq_platform_markers_label",
                table: "platform_markers",
                column: "label",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_markers");

            migrationBuilder.Sql("DROP SCHEMA IF EXISTS hangfire CASCADE;");
        }
    }
}
