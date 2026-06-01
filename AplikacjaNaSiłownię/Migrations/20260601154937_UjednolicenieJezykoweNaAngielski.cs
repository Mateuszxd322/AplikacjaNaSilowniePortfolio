using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AplikacjaNaSiłownię.Migrations
{
    /// <inheritdoc />
    public partial class UjednolicenieJezykoweNaAngielski : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_AspNetUsers_TrenerId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_CollaborationHistories_AspNetUsers_TrenerId",
                table: "CollaborationHistories");

            migrationBuilder.RenameColumn(
                name: "TrenerName",
                table: "CollaborationHistories",
                newName: "TrainerName");

            migrationBuilder.RenameColumn(
                name: "TrenerId",
                table: "CollaborationHistories",
                newName: "TrainerId");

            migrationBuilder.RenameIndex(
                name: "IX_CollaborationHistories_TrenerId",
                table: "CollaborationHistories",
                newName: "IX_CollaborationHistories_TrainerId");

            migrationBuilder.RenameColumn(
                name: "TrenerId",
                table: "AspNetUsers",
                newName: "TrainerId");

            migrationBuilder.RenameColumn(
                name: "Rola",
                table: "AspNetUsers",
                newName: "Role");

            migrationBuilder.RenameColumn(
                name: "PendingTrenerId",
                table: "AspNetUsers",
                newName: "PendingTrainerId");

            migrationBuilder.RenameColumn(
                name: "Nazwisko",
                table: "AspNetUsers",
                newName: "LastName");

            migrationBuilder.RenameColumn(
                name: "Imie",
                table: "AspNetUsers",
                newName: "FirstName");

            migrationBuilder.RenameIndex(
                name: "IX_AspNetUsers_TrenerId",
                table: "AspNetUsers",
                newName: "IX_AspNetUsers_TrainerId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_AspNetUsers_TrainerId",
                table: "AspNetUsers",
                column: "TrainerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CollaborationHistories_AspNetUsers_TrainerId",
                table: "CollaborationHistories",
                column: "TrainerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_AspNetUsers_TrainerId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_CollaborationHistories_AspNetUsers_TrainerId",
                table: "CollaborationHistories");

            migrationBuilder.RenameColumn(
                name: "TrainerName",
                table: "CollaborationHistories",
                newName: "TrenerName");

            migrationBuilder.RenameColumn(
                name: "TrainerId",
                table: "CollaborationHistories",
                newName: "TrenerId");

            migrationBuilder.RenameIndex(
                name: "IX_CollaborationHistories_TrainerId",
                table: "CollaborationHistories",
                newName: "IX_CollaborationHistories_TrenerId");

            migrationBuilder.RenameColumn(
                name: "TrainerId",
                table: "AspNetUsers",
                newName: "TrenerId");

            migrationBuilder.RenameColumn(
                name: "Role",
                table: "AspNetUsers",
                newName: "Rola");

            migrationBuilder.RenameColumn(
                name: "PendingTrainerId",
                table: "AspNetUsers",
                newName: "PendingTrenerId");

            migrationBuilder.RenameColumn(
                name: "LastName",
                table: "AspNetUsers",
                newName: "Nazwisko");

            migrationBuilder.RenameColumn(
                name: "FirstName",
                table: "AspNetUsers",
                newName: "Imie");

            migrationBuilder.RenameIndex(
                name: "IX_AspNetUsers_TrainerId",
                table: "AspNetUsers",
                newName: "IX_AspNetUsers_TrenerId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_AspNetUsers_TrenerId",
                table: "AspNetUsers",
                column: "TrenerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CollaborationHistories_AspNetUsers_TrenerId",
                table: "CollaborationHistories",
                column: "TrenerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
