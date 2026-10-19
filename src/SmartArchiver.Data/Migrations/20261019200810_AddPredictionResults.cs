using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartArchiver.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPredictionResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments");

            migrationBuilder.CreateTable(
                name: "PredictionResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExperimentId = table.Column<int>(type: "int", nullable: false),
                    Group = table.Column<byte>(type: "tinyint", nullable: false),
                    OriginalBytes = table.Column<long>(type: "bigint", nullable: false),
                    OriginalEntropy = table.Column<double>(type: "float", nullable: false),
                    ResidualEntropy = table.Column<double>(type: "float", nullable: false),
                    EncodedBytes = table.Column<long>(type: "bigint", nullable: false),
                    PredictedEncodedBytes = table.Column<long>(type: "bigint", nullable: false),
                    ElapsedSeconds = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PredictionResults", x => x.Id);
                    table.CheckConstraint("CK_PredictionResults_Entropy", "[OriginalEntropy] BETWEEN 0 AND 8 AND [ResidualEntropy] BETWEEN 0 AND 8");
                    table.CheckConstraint("CK_PredictionResults_Group", "[Group] <= 6");
                    table.CheckConstraint("CK_PredictionResults_Sizes", "[OriginalBytes] >= 0 AND [EncodedBytes] >= 0 AND [PredictedEncodedBytes] >= 0");
                    table.CheckConstraint("CK_PredictionResults_Time", "[ElapsedSeconds] >= 0");
                    table.ForeignKey(
                        name: "FK_PredictionResults_Experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "Experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments",
                sql: "[Kind] IN ('Parsing', 'Packing', 'Training', 'Prediction')");

            migrationBuilder.CreateIndex(
                name: "IX_PredictionResults_ExperimentId",
                table: "PredictionResults",
                column: "ExperimentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PredictionResults");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments",
                sql: "[Kind] IN ('Parsing', 'Packing', 'Training')");
        }
    }
}
