using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AplikacjaNaSiłownię.Migrations
{
    /// <inheritdoc />
    public partial class HistoriaTreningowNaSilowni : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "PlannedWorkouts",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "PlannedWorkouts");
        }
    }
}
