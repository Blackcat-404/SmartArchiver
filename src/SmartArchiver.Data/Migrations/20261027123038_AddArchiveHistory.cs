using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartArchiver.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArchiveHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArchiveOperations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FileCount = table.Column<int>(type: "int", nullable: false),
                    CompressedFiles = table.Column<int>(type: "int", nullable: false),
                    OriginalBytes = table.Column<long>(type: "bigint", nullable: false),
                    ArchiveBytes = table.Column<long>(type: "bigint", nullable: false),
                    VolumeCount = table.Column<int>(type: "int", nullable: false),
                    ElapsedSeconds = table.Column<double>(type: "float", nullable: false),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    ErrorCode = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveOperations", x => x.Id);
                    table.CheckConstraint("CK_ArchiveOperations_Error", "([Succeeded] = 1 AND [ErrorCode] IS NULL) OR ([Succeeded] = 0 AND [ErrorCode] IS NOT NULL AND LEN([ErrorCode]) > 0)");
                    table.CheckConstraint("CK_ArchiveOperations_Files", "[FileCount] >= 0 AND [CompressedFiles] BETWEEN 0 AND [FileCount]");
                    table.CheckConstraint("CK_ArchiveOperations_Operation", "[Operation] IN ('Pack', 'Unpack')");
                    table.CheckConstraint("CK_ArchiveOperations_Sizes", "[OriginalBytes] >= 0 AND [ArchiveBytes] >= 0 AND [VolumeCount] >= 0");
                    table.CheckConstraint("CK_ArchiveOperations_Time", "[ElapsedSeconds] >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveOperations_StartedAtUtc",
                table: "ArchiveOperations",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchiveOperations");
        }
    }
}
