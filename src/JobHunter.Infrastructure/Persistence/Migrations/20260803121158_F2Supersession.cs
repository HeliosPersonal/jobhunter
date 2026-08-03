using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds <c>jobs.superseded_by</c> for the reprocessing lifecycle (AC-09, T09): when a normalisation-rule
    /// change moves a job's fingerprint, the old row is retired to <c>Superseded</c> and points at the new
    /// job carrying the opening rather than being deleted, so downstream references resolve to a successor
    /// rather than dangling. Nullable — set only on a superseded row.
    /// </summary>
    public partial class F2Supersession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "superseded_by",
                table: "jobs",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "superseded_by",
                table: "jobs");
        }
    }
}
