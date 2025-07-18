using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoClient.Migrations
{
    /// <inheritdoc />
    public partial class AddExitDateToService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExitDate",
                table: "Services",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExitDate",
                table: "Services");
        }
    }
}
