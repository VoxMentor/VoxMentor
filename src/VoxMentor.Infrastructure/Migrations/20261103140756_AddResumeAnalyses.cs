using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxMentor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResumeAnalyses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ResumeAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    JdId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    MatchScore = table.Column<int>(type: "integer", nullable: false),
                    KeywordMatchJson = table.Column<string>(type: "text", nullable: false),
                    AtsParseabilityJson = table.Column<string>(type: "text", nullable: false),
                    SuggestionsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResumeAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResumeAnalyses_JobDescriptions_JdId",
                        column: x => x.JdId,
                        principalTable: "JobDescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResumeAnalyses_JdId",
                table: "ResumeAnalyses",
                column: "JdId");

            migrationBuilder.CreateIndex(
                name: "IX_ResumeAnalyses_UserId_CreatedAt",
                table: "ResumeAnalyses",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResumeAnalyses");
        }
    }
}
