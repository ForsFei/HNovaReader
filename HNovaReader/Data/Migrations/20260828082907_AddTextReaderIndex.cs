using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HNovaReader.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTextReaderIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookTextIndexes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BookId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EncodingName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    LastWriteTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PatternVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalCharacterCount = table.Column<long>(type: "INTEGER", nullable: false),
                    IsComplete = table.Column<bool>(type: "INTEGER", nullable: false),
                    BuiltAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookTextIndexes", x => x.Id);
                    table.CheckConstraint("CK_BookTextIndexes_FileSize_NonNegative", "FileSize >= 0");
                    table.CheckConstraint("CK_BookTextIndexes_PatternVersion_Positive", "PatternVersion > 0");
                    table.CheckConstraint("CK_BookTextIndexes_TotalCharacterCount_NonNegative", "TotalCharacterCount >= 0");
                    table.ForeignKey(
                        name: "FK_BookTextIndexes_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Chapters",
                columns: table => new
                {
                    BookTextIndexId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 240, nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    StartOffset = table.Column<long>(type: "INTEGER", nullable: false),
                    EndOffset = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chapters", x => new { x.BookTextIndexId, x.Ordinal });
                    table.CheckConstraint("CK_Chapters_Level_Positive", "Level > 0");
                    table.CheckConstraint("CK_Chapters_Offsets_Valid", "StartOffset >= 0 AND EndOffset >= StartOffset");
                    table.CheckConstraint("CK_Chapters_Ordinal_NonNegative", "Ordinal >= 0");
                    table.ForeignKey(
                        name: "FK_Chapters_BookTextIndexes_BookTextIndexId",
                        column: x => x.BookTextIndexId,
                        principalTable: "BookTextIndexes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TextCheckpoints",
                columns: table => new
                {
                    BookTextIndexId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterOffset = table.Column<long>(type: "INTEGER", nullable: false),
                    ByteOffset = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TextCheckpoints", x => new { x.BookTextIndexId, x.CharacterOffset });
                    table.CheckConstraint("CK_TextCheckpoints_ByteOffset_NonNegative", "ByteOffset >= 0");
                    table.CheckConstraint("CK_TextCheckpoints_CharacterOffset_NonNegative", "CharacterOffset >= 0");
                    table.ForeignKey(
                        name: "FK_TextCheckpoints_BookTextIndexes_BookTextIndexId",
                        column: x => x.BookTextIndexId,
                        principalTable: "BookTextIndexes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookTextIndexes_BookId",
                table: "BookTextIndexes",
                column: "BookId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_BookTextIndexId_StartOffset",
                table: "Chapters",
                columns: new[] { "BookTextIndexId", "StartOffset" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Chapters");

            migrationBuilder.DropTable(
                name: "TextCheckpoints");

            migrationBuilder.DropTable(
                name: "BookTextIndexes");
        }
    }
}
