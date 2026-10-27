using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartArchiver.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGeneticJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments");

            migrationBuilder.CreateTable(
                name: "GeneticIndividuals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExperimentId = table.Column<int>(type: "int", nullable: false),
                    Algorithm = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Generation = table.Column<int>(type: "int", nullable: false),
                    Individual = table.Column<int>(type: "int", nullable: false),
                    GenesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Fitness = table.Column<double>(type: "float", nullable: false),
                    S = table.Column<double>(type: "float", nullable: false),
                    T = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneticIndividuals", x => x.Id);
                    table.CheckConstraint("CK_GeneticIndividuals_Algorithm", "[Algorithm] IN ('Genetic', 'Random', 'LocalSearch')");
                    table.CheckConstraint("CK_GeneticIndividuals_Fitness", "[Fitness] >= 0 AND [S] BETWEEN 0 AND 1 AND [T] BETWEEN 0 AND 1");
                    table.CheckConstraint("CK_GeneticIndividuals_Genes", "ISJSON([GenesJson]) = 1 AND LEFT([GenesJson], 1) = '{'");
                    table.CheckConstraint("CK_GeneticIndividuals_Position", "[Generation] >= 0 AND [Individual] >= 0");
                    table.ForeignKey(
                        name: "FK_GeneticIndividuals_Experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "Experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments",
                sql: "[Kind] IN ('Parsing', 'Packing', 'Training', 'Prediction', 'Strategy', 'Genetic')");

            migrationBuilder.CreateIndex(
                name: "IX_GeneticIndividuals_ExperimentId_Algorithm_Generation_Individual",
                table: "GeneticIndividuals",
                columns: new[] { "ExperimentId", "Algorithm", "Generation", "Individual" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeneticIndividuals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Experiments_Kind",
                table: "Experiments",
                sql: "[Kind] IN ('Parsing', 'Packing', 'Training', 'Prediction', 'Strategy')");
        }
    }
}
