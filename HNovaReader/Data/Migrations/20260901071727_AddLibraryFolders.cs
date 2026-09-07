using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HNovaReader.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Series_NormalizedName",
                table: "Series");

            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FolderId",
                table: "Books",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LibraryFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryFolders", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "LibraryFolders",
                columns: new[] { "Id", "Name", "NormalizedName", "SortOrder", "IsSystem" },
                values: new object[]
                {
                    new Guid("7d407eab-d89d-4b7e-aed6-e69f43c60e71"),
                    "收件箱",
                    "收件箱",
                    int.MinValue,
                    true
                });

            migrationBuilder.Sql(
                """
                UPDATE Series
                SET FolderId = '7D407EAB-D89D-4B7E-AED6-E69F43C60E71'
                WHERE FolderId IS NULL;

                UPDATE Books
                SET FolderId = COALESCE(
                    (SELECT FolderId FROM Series WHERE Series.Id = Books.SeriesId),
                    '7D407EAB-D89D-4B7E-AED6-E69F43C60E71')
                WHERE FolderId IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Series_FolderId_NormalizedName",
                table: "Series",
                columns: new[] { "FolderId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Books_FolderId",
                table: "Books",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryFolders_NormalizedName",
                table: "LibraryFolders",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryFolders_SortOrder",
                table: "LibraryFolders",
                column: "SortOrder");

            migrationBuilder.AddForeignKey(
                name: "FK_Books_LibraryFolders_FolderId",
                table: "Books",
                column: "FolderId",
                principalTable: "LibraryFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Series_LibraryFolders_FolderId",
                table: "Series",
                column: "FolderId",
                principalTable: "LibraryFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Books_LibraryFolders_FolderId",
                table: "Books");

            migrationBuilder.DropForeignKey(
                name: "FK_Series_LibraryFolders_FolderId",
                table: "Series");

            migrationBuilder.DropTable(
                name: "LibraryFolders");

            migrationBuilder.DropIndex(
                name: "IX_Series_FolderId_NormalizedName",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_Books_FolderId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "Books");

            migrationBuilder.CreateIndex(
                name: "IX_Series_NormalizedName",
                table: "Series",
                column: "NormalizedName",
                unique: true);
        }
    }
}
