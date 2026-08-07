using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F10AddCommandInvocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "command_invocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    command = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    arg_count = table.Column<short>(type: "smallint", nullable: false),
                    invoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_command_invocations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_command_invocations_command",
                table: "command_invocations",
                columns: new[] { "command", "invoked_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_command_invocations_outcome",
                table: "command_invocations",
                columns: new[] { "outcome", "invoked_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_command_invocations_time",
                table: "command_invocations",
                column: "invoked_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "command_invocations");
        }
    }
}
