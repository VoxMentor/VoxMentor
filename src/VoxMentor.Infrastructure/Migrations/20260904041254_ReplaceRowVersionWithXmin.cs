using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxMentor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceRowVersionWithXmin : Migration
    {
        // NOTE: hand-edited. EF scaffolded a RenameColumn to "xmin", but xmin is a
        // PostgreSQL system column — it cannot be created or renamed to. Npgsql maps
        // the uint RowVersion directly to the built-in xmin, so Up only drops the
        // obsolete bytea column.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "StudentMasteries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "StudentMasteries",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }
    }
}
