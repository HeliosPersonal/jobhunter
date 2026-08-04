using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F4AddReMatchBacklog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "re_match_queue",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cv_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier = table.Column<string>(type: "text", nullable: false),
                    enqueued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_re_match_queue", x => x.id);
                    table.ForeignKey(
                        name: "FK_re_match_queue_cv_versions_cv_version_id",
                        column: x => x.cv_version_id,
                        principalTable: "cv_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_re_match_queue_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_re_match_queue_cv_version_id",
                table: "re_match_queue",
                column: "cv_version_id");

            migrationBuilder.CreateIndex(
                name: "uq_re_match_queue_open",
                table: "re_match_queue",
                column: "job_id",
                unique: true,
                filter: "NOT consumed");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "re_match_queue");
        }
    }
}
