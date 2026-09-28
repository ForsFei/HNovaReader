using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HNovaReader.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHierarchicalLibraryFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Series_LibraryFolders_FolderId",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_Series_FolderId_NormalizedName",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_LibraryFolders_NormalizedName",
                table: "LibraryFolders");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedRelativePath",
                table: "LibraryFolders",
                type: "TEXT",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "LibraryFolders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelativePath",
                table: "LibraryFolders",
                type: "TEXT",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            // Existing folders become top-level physical paths; series duplicates merge by identity.
            migrationBuilder.Sql("""
                UPDATE LibraryFolders SET RelativePath = Name, NormalizedRelativePath = NormalizedName;
                UPDATE Books SET SeriesId = (
                    SELECT MIN(s2.Id) FROM Series s1 JOIN Series s2
                    ON s2.NormalizedName = s1.NormalizedName WHERE s1.Id = Books.SeriesId
                ) WHERE SeriesId IS NOT NULL;
                DELETE FROM Series WHERE Id NOT IN (SELECT MIN(Id) FROM Series GROUP BY NormalizedName);
                UPDATE Series SET FolderId = NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Series_NormalizedName",
                table: "Series",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryFolders_NormalizedRelativePath",
                table: "LibraryFolders",
                column: "NormalizedRelativePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryFolders_ParentId",
                table: "LibraryFolders",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_LibraryFolders_LibraryFolders_ParentId",
                table: "LibraryFolders",
                column: "ParentId",
                principalTable: "LibraryFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LibraryFolders_LibraryFolders_ParentId",
                table: "LibraryFolders");

            migrationBuilder.DropIndex(
                name: "IX_Series_NormalizedName",
                table: "Series");

            migrationBuilder.DropIndex(
                name: "IX_LibraryFolders_NormalizedRelativePath",
                table: "LibraryFolders");

            migrationBuilder.DropIndex(
                name: "IX_LibraryFolders_ParentId",
                table: "LibraryFolders");

            migrationBuilder.DropColumn(
                name: "NormalizedRelativePath",
                table: "LibraryFolders");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "LibraryFolders");

            migrationBuilder.DropColumn(
                name: "RelativePath",
                table: "LibraryFolders");

            migrationBuilder.CreateIndex(
                name: "IX_Series_FolderId_NormalizedName",
                table: "Series",
                columns: new[] { "FolderId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryFolders_NormalizedName",
                table: "LibraryFolders",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Series_LibraryFolders_FolderId",
                table: "Series",
                column: "FolderId",
                principalTable: "LibraryFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
