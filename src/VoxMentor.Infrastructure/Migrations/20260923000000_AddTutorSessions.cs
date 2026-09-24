using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxMentor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTutorSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TutorSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ConceptId = table.Column<Guid>(type: "uuid", nullable: true),
                    Question = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Answer = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TotalTokens = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TutorSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TutorSessions_UserId_CreatedAt",
                table: "TutorSessions",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TutorSessions");
        }
    }
}
