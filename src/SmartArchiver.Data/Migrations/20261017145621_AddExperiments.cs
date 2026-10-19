using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartArchiver.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExperiments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Experiments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CommitHash = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    Seed = table.Column<int>(type: "int", nullable: true),
                    Dataset = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Experiments", x => x.Id);
                    table.CheckConstraint("CK_Experiments_Dataset", "LEN([Dataset]) > 0");
                    table.CheckConstraint("CK_Experiments_Kind", "[Kind] IN ('Parsing', 'Packing', 'Training')");
                });

            migrationBuilder.CreateTable(
                name: "PackingResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExperimentId = table.Column<int>(type: "int", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    VolumeIndex = table.Column<int>(type: "int", nullable: false),
                    CapacityBytes = table.Column<long>(type: "bigint", nullable: false),
                    UsedBytes = table.Column<long>(type: "bigint", nullable: false),
                    TotalValue = table.Column<double>(type: "float", nullable: false),
                    BlockCount = table.Column<int>(type: "int", nullable: false),
                    ElapsedSeconds = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackingResults", x => x.Id);
                    table.CheckConstraint("CK_PackingResults_Capacity", "[UsedBytes] >= 0 AND [UsedBytes] <= [CapacityBytes]");
                    table.CheckConstraint("CK_PackingResults_Method", "[Method] IN ('Dp', 'Greedy')");
                    table.CheckConstraint("CK_PackingResults_Value", "[TotalValue] >= 0 AND [ElapsedSeconds] >= 0");
                    table.CheckConstraint("CK_PackingResults_Volume", "[VolumeIndex] >= 0 AND [BlockCount] >= 0");
                    table.ForeignKey(
                        name: "FK_PackingResults_Experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "Experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParsingResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExperimentId = table.Column<int>(type: "int", nullable: false),
                    Group = table.Column<byte>(type: "tinyint", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OriginalBytes = table.Column<long>(type: "bigint", nullable: false),
                    EncodedBytes = table.Column<long>(type: "bigint", nullable: false),
                    ElapsedSeconds = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParsingResults", x => x.Id);
                    table.CheckConstraint("CK_ParsingResults_Group", "[Group] <= 6");
                    table.CheckConstraint("CK_ParsingResults_Method", "[Method] IN ('Greedy', 'Lazy', 'Dp')");
                    table.CheckConstraint("CK_ParsingResults_Sizes", "[OriginalBytes] >= 0 AND [EncodedBytes] >= 0");
                    table.CheckConstraint("CK_ParsingResults_Time", "[ElapsedSeconds] >= 0");
                    table.ForeignKey(
                        name: "FK_ParsingResults_Experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "Experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrainingRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExperimentId = table.Column<int>(type: "int", nullable: false),
                    InitSeed = table.Column<int>(type: "int", nullable: false),
                    LayerSizes = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    Epochs = table.Column<int>(type: "int", nullable: false),
                    LearningRate = table.Column<double>(type: "float", nullable: false),
                    TrainLoss = table.Column<double>(type: "float", nullable: false),
                    TrainMacroF1 = table.Column<double>(type: "float", nullable: false),
                    ValidMacroF1 = table.Column<double>(type: "float", nullable: false),
                    ConfusionMatrixJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSelected = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingRuns", x => x.Id);
                    table.CheckConstraint("CK_TrainingRuns_LayerSizes", "LEN([LayerSizes]) > 0");
                    table.CheckConstraint("CK_TrainingRuns_MacroF1", "[TrainMacroF1] BETWEEN 0 AND 1 AND [ValidMacroF1] BETWEEN 0 AND 1");
                    table.CheckConstraint("CK_TrainingRuns_Training", "[Epochs] > 0 AND [LearningRate] > 0 AND [TrainLoss] >= 0");
                    table.ForeignKey(
                        name: "FK_TrainingRuns_Experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "Experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_MeasurementRuns_Q",
                table: "MeasurementRuns",
                sql: "[Q] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MeasurementRuns_Sizes",
                table: "MeasurementRuns",
                sql: "[OriginalBytes] >= 0 AND [ArchiveBytes] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MeasurementRuns_Time",
                table: "MeasurementRuns",
                sql: "[ElapsedSeconds] >= 0 AND [TRefSeconds] > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MeasurementRuns_Weights",
                table: "MeasurementRuns",
                sql: "[Alpha] >= 0 AND [Beta] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MeasurementFileResults_Group",
                table: "MeasurementFileResults",
                sql: "[Group] <= 6");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MeasurementFileResults_Sizes",
                table: "MeasurementFileResults",
                sql: "[OriginalBytes] >= 0 AND [ArchiveBytes] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_Experiments_Kind_StartedAtUtc",
                table: "Experiments",
                columns: new[] { "Kind", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PackingResults_ExperimentId",
                table: "PackingResults",
                column: "ExperimentId");

            migrationBuilder.CreateIndex(
                name: "IX_ParsingResults_ExperimentId",
                table: "ParsingResults",
                column: "ExperimentId");

            migrationBuilder.CreateIndex(
                name: "IX_TrainingRuns_ExperimentId",
                table: "TrainingRuns",
                column: "ExperimentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackingResults");

            migrationBuilder.DropTable(
                name: "ParsingResults");

            migrationBuilder.DropTable(
                name: "TrainingRuns");

            migrationBuilder.DropTable(
                name: "Experiments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MeasurementRuns_Q",
                table: "MeasurementRuns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MeasurementRuns_Sizes",
                table: "MeasurementRuns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MeasurementRuns_Time",
                table: "MeasurementRuns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MeasurementRuns_Weights",
                table: "MeasurementRuns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MeasurementFileResults_Group",
                table: "MeasurementFileResults");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MeasurementFileResults_Sizes",
                table: "MeasurementFileResults");
        }
    }
}
