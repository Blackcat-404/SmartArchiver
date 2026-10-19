using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartArchiver.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MeasurementRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CommitHash = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    Seed = table.Column<int>(type: "int", nullable: true),
                    Dataset = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Alpha = table.Column<double>(type: "float", nullable: false),
                    Beta = table.Column<double>(type: "float", nullable: false),
                    TRefSeconds = table.Column<double>(type: "float", nullable: false),
                    ParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalBytes = table.Column<long>(type: "bigint", nullable: false),
                    ArchiveBytes = table.Column<long>(type: "bigint", nullable: false),
                    ElapsedSeconds = table.Column<double>(type: "float", nullable: false),
                    AllRestored = table.Column<bool>(type: "bit", nullable: false),
                    Q = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MeasurementFileResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<int>(type: "int", nullable: false),
                    Group = table.Column<byte>(type: "tinyint", nullable: false),
                    OriginalBytes = table.Column<long>(type: "bigint", nullable: false),
                    ArchiveBytes = table.Column<long>(type: "bigint", nullable: false),
                    Restored = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementFileResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeasurementFileResults_MeasurementRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "MeasurementRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementFileResults_RunId",
                table: "MeasurementFileResults",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MeasurementFileResults");

            migrationBuilder.DropTable(
                name: "MeasurementRuns");
        }
    }
}
