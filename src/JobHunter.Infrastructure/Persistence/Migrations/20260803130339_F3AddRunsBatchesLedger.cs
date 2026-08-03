using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F3AddRunsBatchesLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    cutoff_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cutoff_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ceiling_usd = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    spent_usd = table.Column<decimal>(type: "numeric(8,4)", nullable: false, defaultValue: 0m),
                    jobs_in_scope = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    jobs_carried_over = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage = table.Column<string>(type: "text", nullable: false),
                    tier = table.Column<string>(type: "text", nullable: false),
                    provider_batch_id = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    prompt_version = table.Column<string>(type: "text", nullable: false),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: true),
                    output_tokens = table.Column<int>(type: "integer", nullable: true),
                    poll_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batches", x => x.id);
                    table.ForeignKey(
                        name: "FK_batches_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "batch_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    custom_id = table.Column<string>(type: "text", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    raw_result = table.Column<string>(type: "jsonb", nullable: true),
                    parse_error = table.Column<string>(type: "text", nullable: true),
                    retry_count = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_batch_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_batch_items_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_batch_items_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cost_ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage = table.Column<string>(type: "text", nullable: false),
                    tier = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    cost_usd = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: false),
                    output_tokens = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cost_ledger_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_cost_ledger_entries_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_cost_ledger_entries_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_batch_items_retry",
                table: "batch_items",
                columns: new[] { "state", "retry_count" },
                filter: "state = 'ParseFailed'");

            migrationBuilder.CreateIndex(
                name: "IX_batch_items_job_id",
                table: "batch_items",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "uq_batch_items",
                table: "batch_items",
                columns: new[] { "batch_id", "custom_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_batches_pending",
                table: "batches",
                columns: new[] { "state", "submitted_at" },
                filter: "state IN ('Submitted','InProgress')");

            migrationBuilder.CreateIndex(
                name: "uq_batches_run_stage_tier",
                table: "batches",
                columns: new[] { "run_id", "stage", "tier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_cost_ledger_run",
                table: "cost_ledger_entries",
                columns: new[] { "run_id", "stage", "tier" });

            migrationBuilder.CreateIndex(
                name: "IX_cost_ledger_entries_batch_id",
                table: "cost_ledger_entries",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "idx_runs_delivered",
                table: "runs",
                column: "finished_at",
                filter: "state = 'Delivered'");

            // The two Run indexes that carry QG-1 (data-model §Indexes). EF cannot model a partial
            // unique index over a constant expression, so both are declared here in raw SQL and named
            // for documentation in RunConfiguration.
            //
            // uq_runs_single_active — unique on the constant `(true)` filtered to the non-terminal
            // states, so at most one live Run can exist. A second Run started after a botched restart
            // fails loudly at commit instead of racing the first.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX uq_runs_single_active ON runs ((true)) " +
                "WHERE state NOT IN ('Delivered', 'Failed', 'CostAborted');");

            // idx_runs_resumable — the same predicate, non-unique, so the startup resume sweep is an
            // index scan over exactly the non-terminal Runs rather than a sequential scan of the table.
            migrationBuilder.Sql(
                "CREATE INDEX idx_runs_resumable ON runs (state) " +
                "WHERE state NOT IN ('Delivered', 'Failed', 'CostAborted');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS idx_runs_resumable;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS uq_runs_single_active;");

            migrationBuilder.DropTable(
                name: "batch_items");

            migrationBuilder.DropTable(
                name: "cost_ledger_entries");

            migrationBuilder.DropTable(
                name: "batches");

            migrationBuilder.DropTable(
                name: "runs");
        }
    }
}
