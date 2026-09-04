using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxMentor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentMasteryRowVersion : Migration
    {
        /// <summary>
        /// Adds the bytea RowVersion concurrency column to StudentMasteries
        /// (superseded by 20260904041254_ReplaceRowVersionWithXmin, which drops it in
        /// favor of the xmin system column).
        /// </summary>
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "StudentMasteries",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <summary>Removes the RowVersion concurrency column from StudentMasteries.</summary>
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "StudentMasteries");
        }
    }
}
