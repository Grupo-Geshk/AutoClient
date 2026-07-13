using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoClient.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceWorkshop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La columna ya existía en la BD (creada fuera de migraciones como
            // uuid NOT NULL, con GUIDs vacíos). Se crea solo si falta, se permite
            // NULL y los GUIDs vacíos pasan a NULL (= factura sin taller asociado,
            // que conserva el encabezado legado).
            migrationBuilder.Sql("""
                ALTER TABLE "Invoices" ADD COLUMN IF NOT EXISTS "WorkshopId" uuid;
                ALTER TABLE "Invoices" ALTER COLUMN "WorkshopId" DROP NOT NULL;
                UPDATE "Invoices" SET "WorkshopId" = NULL
                    WHERE "WorkshopId" = '00000000-0000-0000-0000-000000000000';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkshopId",
                table: "Invoices");
        }
    }
}
