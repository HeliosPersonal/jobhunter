using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F3NullableLedgerBatchId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cost_ledger_entries_batches_batch_id",
                table: "cost_ledger_entries");

            migrationBuilder.AlterColumn<Guid>(
                name: "batch_id",
                table: "cost_ledger_entries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_cost_ledger_entries_batches_batch_id",
                table: "cost_ledger_entries",
                column: "batch_id",
                principalTable: "batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cost_ledger_entries_batches_batch_id",
                table: "cost_ledger_entries");

            migrationBuilder.AlterColumn<Guid>(
                name: "batch_id",
                table: "cost_ledger_entries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_cost_ledger_entries_batches_batch_id",
                table: "cost_ledger_entries",
                column: "batch_id",
                principalTable: "batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
