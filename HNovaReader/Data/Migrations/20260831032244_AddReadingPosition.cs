using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HNovaReader.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReadingPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReadingPositions",
                columns: table => new
                {
                    BookId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterOffset = table.Column<long>(type: "INTEGER", nullable: false),
                    ByteOffsetHint = table.Column<long>(type: "INTEGER", nullable: true),
                    Progress = table.Column<double>(type: "REAL", nullable: false),
                    ChapterTitleSnapshot = table.Column<string>(type: "TEXT", maxLength: 240, nullable: true),
                    AnchorPrefix = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    AnchorSuffix = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    LastWriteTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EncodingName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingPositions", x => x.BookId);
                    table.CheckConstraint("CK_ReadingPositions_ByteOffsetHint_NonNegative", "ByteOffsetHint IS NULL OR ByteOffsetHint >= 0");
                    table.CheckConstraint("CK_ReadingPositions_CharacterOffset_NonNegative", "CharacterOffset >= 0");
                    table.CheckConstraint("CK_ReadingPositions_FileSize_NonNegative", "FileSize >= 0");
                    table.CheckConstraint("CK_ReadingPositions_Progress_Range", "Progress >= 0 AND Progress <= 1");
                    table.ForeignKey(
                        name: "FK_ReadingPositions_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingPositions_UpdatedAtUtc",
                table: "ReadingPositions",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReadingPositions");
        }
    }
}
