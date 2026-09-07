using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HNovaReader.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookSourcePath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedSourceFilePath",
                table: "Books",
                type: "TEXT",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceFilePath",
                table: "Books",
                type: "TEXT",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE Books
                SET SourceFilePath = FilePath,
                    NormalizedSourceFilePath = NormalizedFilePath
                WHERE SourceFilePath = '' OR NormalizedSourceFilePath = '';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Books_NormalizedSourceFilePath",
                table: "Books",
                column: "NormalizedSourceFilePath",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Books_NormalizedSourceFilePath",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "NormalizedSourceFilePath",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "SourceFilePath",
                table: "Books");
        }
    }
}
