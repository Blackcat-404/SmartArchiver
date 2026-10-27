using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartArchiver.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerBlockStrategy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StrategyResults_Variant",
                table: "StrategyResults");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StrategyResults_Variant",
                table: "StrategyResults",
                sql: "[Variant] IN ('Stored', 'Fixed', 'Classifier', 'ClassifierPerBlock')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StrategyResults_Variant",
                table: "StrategyResults");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StrategyResults_Variant",
                table: "StrategyResults",
                sql: "[Variant] IN ('Stored', 'Fixed', 'Classifier')");
        }
    }
}
