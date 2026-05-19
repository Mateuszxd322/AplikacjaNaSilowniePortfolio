using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AplikacjaNaSiłownię.Migrations
{
    /// <inheritdoc />
    public partial class usuwanieHistoriiWspolprac : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeletedByClient",
                table: "CollaborationHistories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeletedByTrainer",
                table: "CollaborationHistories",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDeletedByClient",
                table: "CollaborationHistories");

            migrationBuilder.DropColumn(
                name: "IsDeletedByTrainer",
                table: "CollaborationHistories");
        }
    }
}
