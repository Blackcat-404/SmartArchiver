using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartArchiver.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompressionStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BlockImpactResults_Block",
                table: "BlockImpactResults");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BlockImpactResults_Block",
                table: "BlockImpactResults",
                sql: "[DisabledBlock] IN ('None', 'Analysis', 'Lz77', 'Huffman', 'Compression', 'Packing', 'Encryption', 'Prediction')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BlockImpactResults_Block",
                table: "BlockImpactResults");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BlockImpactResults_Block",
                table: "BlockImpactResults",
                sql: "[DisabledBlock] IN ('None', 'Analysis', 'Lz77', 'Huffman', 'Packing', 'Encryption', 'Prediction')");
        }
    }
}
