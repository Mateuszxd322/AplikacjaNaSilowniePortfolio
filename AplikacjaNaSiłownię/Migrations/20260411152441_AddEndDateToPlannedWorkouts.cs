using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AplikacjaNaSiłownię.Migrations
{
    /// <inheritdoc />
    public partial class AddEndDateToPlannedWorkouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EndDate",
                table: "PlannedWorkouts",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "PlannedWorkouts");
        }
    }
}
