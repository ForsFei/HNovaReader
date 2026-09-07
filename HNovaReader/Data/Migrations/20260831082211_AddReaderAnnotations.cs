using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HNovaReader.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReaderAnnotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bookmarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BookId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterOffset = table.Column<long>(type: "INTEGER", nullable: false),
                    ChapterTitleSnapshot = table.Column<string>(type: "TEXT", maxLength: 240, nullable: true),
                    PreviewText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AnchorPrefix = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    AnchorSuffix = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    LastWriteTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EncodingName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookmarks", x => x.Id);
                    table.CheckConstraint("CK_Bookmarks_CharacterOffset_NonNegative", "CharacterOffset >= 0");
                    table.CheckConstraint("CK_Bookmarks_FileSize_NonNegative", "FileSize >= 0");
                    table.ForeignKey(
                        name: "FK_Bookmarks_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Highlights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BookId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartOffset = table.Column<long>(type: "INTEGER", nullable: false),
                    Length = table.Column<int>(type: "INTEGER", nullable: false),
                    SelectedText = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: false),
                    Style = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AnchorPrefix = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    AnchorSuffix = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    LastWriteTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EncodingName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Highlights", x => x.Id);
                    table.CheckConstraint("CK_Highlights_FileSize_NonNegative", "FileSize >= 0");
                    table.CheckConstraint("CK_Highlights_Length_Positive", "Length > 0");
                    table.CheckConstraint("CK_Highlights_StartOffset_NonNegative", "StartOffset >= 0");
                    table.ForeignKey(
                        name: "FK_Highlights_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BookId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HighlightId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CharacterOffset = table.Column<long>(type: "INTEGER", nullable: true),
                    PreviewText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    AnchorPrefix = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    AnchorSuffix = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    LastWriteTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EncodingName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notes", x => x.Id);
                    table.CheckConstraint("CK_Notes_Anchor_ExactlyOne", "(HighlightId IS NOT NULL AND CharacterOffset IS NULL) OR (HighlightId IS NULL AND CharacterOffset IS NOT NULL)");
                    table.CheckConstraint("CK_Notes_CharacterOffset_NonNegative", "CharacterOffset IS NULL OR CharacterOffset >= 0");
                    table.CheckConstraint("CK_Notes_FileSize_NonNegative", "FileSize >= 0");
                    table.ForeignKey(
                        name: "FK_Notes_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Notes_Highlights_HighlightId",
                        column: x => x.HighlightId,
                        principalTable: "Highlights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_BookId_CharacterOffset",
                table: "Bookmarks",
                columns: new[] { "BookId", "CharacterOffset" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_CreatedAtUtc",
                table: "Bookmarks",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Highlights_BookId_StartOffset",
                table: "Highlights",
                columns: new[] { "BookId", "StartOffset" });

            migrationBuilder.CreateIndex(
                name: "IX_Highlights_CreatedAtUtc",
                table: "Highlights",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_BookId_CharacterOffset",
                table: "Notes",
                columns: new[] { "BookId", "CharacterOffset" });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_HighlightId",
                table: "Notes",
                column: "HighlightId");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_UpdatedAtUtc",
                table: "Notes",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bookmarks");

            migrationBuilder.DropTable(
                name: "Notes");

            migrationBuilder.DropTable(
                name: "Highlights");
        }
    }
}
