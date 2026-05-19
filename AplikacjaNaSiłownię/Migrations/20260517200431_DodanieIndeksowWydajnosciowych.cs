using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AplikacjaNaSiłownię.Migrations
{
    /// <inheritdoc />
    public partial class DodanieIndeksowWydajnosciowych : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PlannedWorkouts_ClientId_StartDate",
                table: "PlannedWorkouts",
                columns: new[] { "ClientId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CollaborationHistories_ClientId",
                table: "CollaborationHistories",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_CollaborationHistories_TrenerId",
                table: "CollaborationHistories",
                column: "TrenerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlannedWorkouts_ClientId_StartDate",
                table: "PlannedWorkouts");

            migrationBuilder.DropIndex(
                name: "IX_CollaborationHistories_ClientId",
                table: "CollaborationHistories");

            migrationBuilder.DropIndex(
                name: "IX_CollaborationHistories_TrenerId",
                table: "CollaborationHistories");
        }
    }
}
