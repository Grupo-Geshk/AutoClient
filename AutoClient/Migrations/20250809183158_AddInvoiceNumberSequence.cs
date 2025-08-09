using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoClient.Migrations;

public partial class AddInvoiceNumberSequence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            CREATE SEQUENCE IF NOT EXISTS invoice_number_seq
            START WITH 1633
            INCREMENT BY 1
            NO MINVALUE
            NO MAXVALUE
            CACHE 1;
        ");

        migrationBuilder.Sql(@"
            SELECT setval(
                'invoice_number_seq',
                GREATEST(
                    (SELECT COALESCE(MAX(""InvoiceNumber""), 0) FROM ""Invoices""),
                    1632
                )
            );
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"DROP SEQUENCE IF EXISTS invoice_number_seq;");
    }
}
