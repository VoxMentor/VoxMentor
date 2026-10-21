using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoxMentor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTextbookJobContentHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "TextbookJobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_TextbookJobs_ContentHash_ConceptId",
                table: "TextbookJobs",
                columns: new[] { "ContentHash", "ConceptId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TextbookJobs_ContentHash_ConceptId",
                table: "TextbookJobs");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "TextbookJobs");
        }
    }
}
