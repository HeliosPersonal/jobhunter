using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F5AddDigestsAndDeliveryLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "delivery_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    card_key = table.Column<string>(type: "text", nullable: false),
                    telegram_message_id = table.Column<long>(type: "bigint", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_log", x => x.id);
                    table.ForeignKey(
                        name: "FK_delivery_log_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "digests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total_new_jobs = table.Column<int>(type: "integer", nullable: false),
                    strong_matches = table.Column<int>(type: "integer", nullable: false),
                    avg_salary_usd = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    suppressed_count = table.Column<int>(type: "integer", nullable: false),
                    carried_over_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    narrative = table.Column<string>(type: "text", nullable: true),
                    narrative_source = table.Column<string>(type: "text", nullable: false),
                    prompt_version = table.Column<string>(type: "text", nullable: true),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    degraded_sources = table.Column<string>(type: "jsonb", nullable: false),
                    suppression_breakdown = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_digests", x => x.id);
                    table.ForeignKey(
                        name: "FK_digests_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "digest_cards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    digest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rank = table.Column<short>(type: "smallint", nullable: false),
                    score = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    apply_url_verified = table.Column<bool>(type: "boolean", nullable: false),
                    card_key = table.Column<string>(type: "text", nullable: false),
                    reasons = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_digest_cards", x => x.id);
                    table.ForeignKey(
                        name: "FK_digest_cards_digests_digest_id",
                        column: x => x.digest_id,
                        principalTable: "digests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_digest_cards_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_delivery_log_run_chat",
                table: "delivery_log",
                columns: new[] { "run_id", "chat_id" });

            migrationBuilder.CreateIndex(
                name: "uq_delivery_log",
                table: "delivery_log",
                columns: new[] { "run_id", "chat_id", "card_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_digest_cards_rank",
                table: "digest_cards",
                columns: new[] { "digest_id", "rank" });

            migrationBuilder.CreateIndex(
                name: "IX_digest_cards_job_id",
                table: "digest_cards",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "uq_digest_cards_job",
                table: "digest_cards",
                columns: new[] { "digest_id", "job_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_digest_cards_key",
                table: "digest_cards",
                columns: new[] { "digest_id", "card_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_digests_run",
                table: "digests",
                column: "run_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_log");

            migrationBuilder.DropTable(
                name: "digest_cards");

            migrationBuilder.DropTable(
                name: "digests");
        }
    }
}
