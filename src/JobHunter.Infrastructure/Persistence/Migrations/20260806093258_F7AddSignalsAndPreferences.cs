using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F7AddSignalsAndPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "preference_models",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    signal_count = table.Column<int>(type: "integer", nullable: false),
                    fitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preference_models", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "signals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    weight = table.Column<decimal>(type: "numeric(3,1)", nullable: false),
                    job_facts = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signals", x => x.id);
                    table.ForeignKey(
                        name: "FK_signals_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "suppression_overrides",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dimension = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppression_overrides", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "preference_weights",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dimension = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    weight = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    positive_rate = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    disabled = table.Column<bool>(type: "boolean", nullable: false),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    supporting_signal_ids = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_preference_weights", x => x.id);
                    table.ForeignKey(
                        name: "FK_preference_weights_preference_models_model_id",
                        column: x => x.model_id,
                        principalTable: "preference_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_preference_models_version",
                table: "preference_models",
                column: "version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_preference_weights_lookup",
                table: "preference_weights",
                columns: new[] { "model_id", "dimension", "value" },
                filter: "NOT disabled");

            migrationBuilder.CreateIndex(
                name: "idx_signals_kind",
                table: "signals",
                columns: new[] { "kind", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_signals_window",
                table: "signals",
                column: "occurred_at",
                descending: Array.Empty<bool>());

            migrationBuilder.CreateIndex(
                name: "uq_signals_action",
                table: "signals",
                columns: new[] { "job_id", "kind", "occurred_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_suppression_overrides",
                table: "suppression_overrides",
                columns: new[] { "dimension", "value" },
                unique: true);

            // Exactly one active preference model at a time (data-model §preference_models). A partial unique
            // index over the constant expression `(is_active)` filtered to the active rows — EF cannot model a
            // unique index over a constant, so it is declared here in raw SQL and named in the configuration for
            // documentation only. A refit's atomic deactivate-then-activate (SAD §4 S6) never trips it.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX uq_preference_models_active ON preference_models ((is_active)) WHERE is_active;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS uq_preference_models_active;");

            migrationBuilder.DropTable(
                name: "preference_weights");

            migrationBuilder.DropTable(
                name: "signals");

            migrationBuilder.DropTable(
                name: "suppression_overrides");

            migrationBuilder.DropTable(
                name: "preference_models");
        }
    }
}
