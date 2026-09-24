using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxMentor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCodeSubmissionMasteryClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MasteryAppliedAt",
                table: "CodeSubmissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "MasteryBefore",
                table: "CodeSubmissions",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "MasteryAfter",
                table: "CodeSubmissions",
                type: "real",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CorrectAttemptsAfter",
                table: "CodeSubmissions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IncorrectAttemptsAfter",
                table: "CodeSubmissions",
                type: "integer",
                nullable: true);

            // Pre-existing graded submissions already applied BKT before claims existed:
            // mark them claimed so linking their id later cannot double-apply (#51).
            migrationBuilder.Sql(
                "UPDATE \"CodeSubmissions\" SET \"MasteryAppliedAt\" = \"CreatedAt\" WHERE \"Status\" <> 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MasteryAppliedAt",
                table: "CodeSubmissions");

            migrationBuilder.DropColumn(
                name: "MasteryBefore",
                table: "CodeSubmissions");

            migrationBuilder.DropColumn(
                name: "MasteryAfter",
                table: "CodeSubmissions");

            migrationBuilder.DropColumn(
                name: "CorrectAttemptsAfter",
                table: "CodeSubmissions");

            migrationBuilder.DropColumn(
                name: "IncorrectAttemptsAfter",
                table: "CodeSubmissions");
        }
    }
}
