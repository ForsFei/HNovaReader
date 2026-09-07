using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HNovaReader.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryFolderUnsortedName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UnsortedName",
                table: "LibraryFolders",
                type: "TEXT",
                maxLength: 160,
                nullable: false,
                defaultValue: "未分系列");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnsortedName",
                table: "LibraryFolders");
        }
    }
}
