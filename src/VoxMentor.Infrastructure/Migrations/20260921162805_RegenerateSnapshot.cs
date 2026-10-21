using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VoxMentor.Infrastructure.Persistence;

#nullable disable

namespace VoxMentor.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Hand-written no-op marker migration: the generated Designer normally
    // carries the discovery pair, so it was never registered with
    // MigrateAsync (#104 review gate: MigrationDiscoveryTests sweep).
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260921162805_RegenerateSnapshot")]
    public partial class RegenerateSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
