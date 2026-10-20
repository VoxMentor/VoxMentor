using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VoxMentor.Infrastructure.Persistence;

#nullable disable

namespace VoxMentor.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Hand-written: EF Core only registers Migration-derived types that
    // carry BOTH [DbContext] and [Migration] — without these MigrateAsync
    // silently skips the migration and never creates the column
    // (#104 CodeRabbit round 5).
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261020124500_AddTextbookJobClaimedAt")]
    public partial class AddTextbookJobClaimedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedAt",
                table: "TextbookJobs",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimedAt",
                table: "TextbookJobs");
        }
    }
}
