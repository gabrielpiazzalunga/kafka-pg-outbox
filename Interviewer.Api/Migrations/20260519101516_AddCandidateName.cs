using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Interviewer.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InterviewSessions_SessionCode",
                table: "InterviewSessions");

            migrationBuilder.DropColumn(
                name: "SessionCode",
                table: "InterviewSessions");

            migrationBuilder.AddColumn<string>(
                name: "CandidateName",
                table: "InterviewSessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CandidateName",
                table: "InterviewSessions");

            migrationBuilder.AddColumn<string>(
                name: "SessionCode",
                table: "InterviewSessions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_InterviewSessions_SessionCode",
                table: "InterviewSessions",
                column: "SessionCode",
                unique: true);
        }
    }
}
