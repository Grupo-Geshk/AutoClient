using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoClient.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotesAndWorkshopProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotente: la BD de producción ya tenía algunas de estas columnas
            // añadidas fuera de las migraciones.
            migrationBuilder.Sql("""
                ALTER TABLE "Workshops" ADD COLUMN IF NOT EXISTS "Address" character varying(300) NULL;
                ALTER TABLE "Workshops" ADD COLUMN IF NOT EXISTS "BusinessDescription" character varying(1000) NULL;
                ALTER TABLE "Workshops" ADD COLUMN IF NOT EXISTS "Dv" character varying(10) NULL;
                ALTER TABLE "Workshops" ADD COLUMN IF NOT EXISTS "Logo" character varying(500) NULL;
                ALTER TABLE "Workshops" ADD COLUMN IF NOT EXISTS "NotificationEmail" character varying(100) NULL;
                ALTER TABLE "Workshops" ADD COLUMN IF NOT EXISTS "Ruc" character varying(30) NULL;
                """);

            migrationBuilder.CreateTable(
                name: "Quotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkshopId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteNumber = table.Column<long>(type: "bigint", nullable: false),
                    QuoteDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    ClientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClientEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClientPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    VehicleInfo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ShareToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Tax = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Quotes_Workshops_WorkshopId",
                        column: x => x.WorkshopId,
                        principalTable: "Workshops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuoteItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(12,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteItems_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteItems_QuoteId",
                table: "QuoteItems",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_ShareToken",
                table: "Quotes",
                column: "ShareToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_WorkshopId_QuoteNumber",
                table: "Quotes",
                columns: new[] { "WorkshopId", "QuoteNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuoteItems");

            migrationBuilder.DropTable(
                name: "Quotes");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Workshops");

            migrationBuilder.DropColumn(
                name: "BusinessDescription",
                table: "Workshops");

            migrationBuilder.DropColumn(
                name: "Dv",
                table: "Workshops");

            migrationBuilder.DropColumn(
                name: "Logo",
                table: "Workshops");

            migrationBuilder.DropColumn(
                name: "NotificationEmail",
                table: "Workshops");

            migrationBuilder.DropColumn(
                name: "Ruc",
                table: "Workshops");
        }
    }
}
