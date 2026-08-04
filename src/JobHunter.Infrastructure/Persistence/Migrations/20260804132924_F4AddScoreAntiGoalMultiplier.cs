using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F4AddScoreAntiGoalMultiplier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default 1.00, not 0: the anti-goal multiplier is neutral for an ordinary role, so any row
            // written before this column existed keeps its meaning (a 0 would silently zero its reconciliation).
            migrationBuilder.AddColumn<decimal>(
                name: "anti_goal_multiplier",
                table: "scores",
                type: "numeric(3,2)",
                nullable: false,
                defaultValue: 1.00m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "anti_goal_multiplier",
                table: "scores");
        }
    }
}
