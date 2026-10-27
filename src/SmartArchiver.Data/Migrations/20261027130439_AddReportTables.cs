using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartArchiver.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReportTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments");

            migrationBuilder.CreateTable(
                name: "BlockImpactResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExperimentId = table.Column<int>(type: "int", nullable: false),
                    DisabledBlock = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
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
                    table.PrimaryKey("PK_BlockImpactResults", x => x.Id);
                    table.CheckConstraint("CK_BlockImpactResults_Block", "[DisabledBlock] IN ('None', 'Analysis', 'Lz77', 'Huffman', 'Packing', 'Encryption', 'Prediction')");
                    table.CheckConstraint("CK_BlockImpactResults_Q", "[S] BETWEEN 0 AND 1 AND [T] BETWEEN 0 AND 1 AND [Q] >= 0");
                    table.CheckConstraint("CK_BlockImpactResults_Restored", "[AllRestored] = 1 OR [Q] = 0");
                    table.CheckConstraint("CK_BlockImpactResults_Sizes", "[OriginalBytes] >= 0 AND [ArchiveBytes] >= 0 AND [ElapsedSeconds] >= 0");
                    table.ForeignKey(
                        name: "FK_BlockImpactResults_Experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "Experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EncryptionOrderResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExperimentId = table.Column<int>(type: "int", nullable: false),
                    Group = table.Column<byte>(type: "tinyint", nullable: false),
                    OriginalBytes = table.Column<long>(type: "bigint", nullable: false),
                    CompressThenEncryptBytes = table.Column<long>(type: "bigint", nullable: false),
                    EncryptThenCompressBytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EncryptionOrderResults", x => x.Id);
                    table.CheckConstraint("CK_EncryptionOrderResults_Group", "[Group] <= 6");
                    table.CheckConstraint("CK_EncryptionOrderResults_Sizes", "[OriginalBytes] >= 0 AND [CompressThenEncryptBytes] >= 0 AND [EncryptThenCompressBytes] >= 0");
                    table.ForeignKey(
                        name: "FK_EncryptionOrderResults_Experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "Experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments",
                sql: "[Kind] IN ('Parsing', 'Packing', 'Training', 'Prediction', 'Strategy', 'Genetic', 'BlockImpact', 'EncryptionOrder')");

            migrationBuilder.CreateIndex(
                name: "IX_BlockImpactResults_ExperimentId_DisabledBlock",
                table: "BlockImpactResults",
                columns: new[] { "ExperimentId", "DisabledBlock" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EncryptionOrderResults_ExperimentId",
                table: "EncryptionOrderResults",
                column: "ExperimentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockImpactResults");

            migrationBuilder.DropTable(
                name: "EncryptionOrderResults");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments",
                sql: "[Kind] IN ('Parsing', 'Packing', 'Training', 'Prediction', 'Strategy', 'Genetic')");
        }
    }
}
