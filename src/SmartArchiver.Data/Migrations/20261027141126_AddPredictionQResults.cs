using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartArchiver.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPredictionQResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments");

            migrationBuilder.CreateTable(
                name: "PredictionQResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExperimentId = table.Column<int>(type: "int", nullable: false),
                    Group = table.Column<byte>(type: "tinyint", nullable: false),
                    Predicted = table.Column<bool>(type: "bit", nullable: false),
                    FileCount = table.Column<int>(type: "int", nullable: false),
                    OriginalBytes = table.Column<long>(type: "bigint", nullable: false),
                    ArchiveBytes = table.Column<long>(type: "bigint", nullable: false),
                    ElapsedSeconds = table.Column<double>(type: "float", nullable: false),
                    AllRestored = table.Column<bool>(type: "bit", nullable: false),
                    S = table.Column<double>(type: "float", nullable: false),
                    T = table.Column<double>(type: "float", nullable: false),
                    Q = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PredictionQResults", x => x.Id);
                    table.CheckConstraint("CK_PredictionQResults_Group", "[Group] <= 6");
                    table.CheckConstraint("CK_PredictionQResults_Q", "[S] BETWEEN 0 AND 1 AND [T] BETWEEN 0 AND 1 AND [Q] >= 0");
                    table.CheckConstraint("CK_PredictionQResults_Restored", "[AllRestored] = 1 OR [Q] = 0");
                    table.CheckConstraint("CK_PredictionQResults_Sizes", "[FileCount] >= 0 AND [OriginalBytes] >= 0 AND [ArchiveBytes] >= 0 AND [ElapsedSeconds] >= 0");
                    table.ForeignKey(
                        name: "FK_PredictionQResults_Experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "Experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments",
                sql: "[Kind] IN ('Parsing', 'Packing', 'Training', 'Prediction', 'Strategy', 'Genetic', 'BlockImpact', 'EncryptionOrder', 'PredictionQ')");

            migrationBuilder.CreateIndex(
                name: "IX_PredictionQResults_ExperimentId_Group_Predicted",
                table: "PredictionQResults",
                columns: new[] { "ExperimentId", "Group", "Predicted" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PredictionQResults");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments",
                sql: "[Kind] IN ('Parsing', 'Packing', 'Training', 'Prediction', 'Strategy', 'Genetic', 'BlockImpact', 'EncryptionOrder')");
        }
    }
}
