using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartArchiver.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments");

            migrationBuilder.CreateTable(
                name: "StrategyResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExperimentId = table.Column<int>(type: "int", nullable: false),
                    Group = table.Column<byte>(type: "tinyint", nullable: false),
                    Variant = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FileCount = table.Column<int>(type: "int", nullable: false),
                    CompressedFiles = table.Column<int>(type: "int", nullable: false),
                    OriginalBytes = table.Column<long>(type: "bigint", nullable: false),
                    ArchiveBytes = table.Column<long>(type: "bigint", nullable: false),
                    PackSeconds = table.Column<double>(type: "float", nullable: false),
                    UnpackSeconds = table.Column<double>(type: "float", nullable: false),
                    AllRestored = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyResults", x => x.Id);
                    table.CheckConstraint("CK_StrategyResults_Files", "[FileCount] >= 0 AND [CompressedFiles] BETWEEN 0 AND [FileCount]");
                    table.CheckConstraint("CK_StrategyResults_Group", "[Group] <= 6");
                    table.CheckConstraint("CK_StrategyResults_Sizes", "[OriginalBytes] >= 0 AND [ArchiveBytes] >= 0");
                    table.CheckConstraint("CK_StrategyResults_Time", "[PackSeconds] >= 0 AND [UnpackSeconds] >= 0");
                    table.CheckConstraint("CK_StrategyResults_Variant", "[Variant] IN ('Stored', 'Fixed', 'Classifier')");
                    table.ForeignKey(
                        name: "FK_StrategyResults_Experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "Experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments",
                sql: "[Kind] IN ('Parsing', 'Packing', 'Training', 'Prediction', 'Strategy')");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyResults_ExperimentId",
                table: "StrategyResults",
                column: "ExperimentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StrategyResults");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments",
                sql: "[Kind] IN ('Parsing', 'Packing', 'Training', 'Prediction')");
        }
    }
}
