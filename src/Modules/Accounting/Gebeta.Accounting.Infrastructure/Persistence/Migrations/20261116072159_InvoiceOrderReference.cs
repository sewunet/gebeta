using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceOrderReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "order_reference",
                schema: "accounting",
                table: "invoice",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_company_id_order_reference",
                schema: "accounting",
                table: "invoice",
                columns: new[] { "company_id", "order_reference" },
                filter: "order_reference IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invoice_company_id_order_reference",
                schema: "accounting",
                table: "invoice");

            migrationBuilder.DropColumn(
                name: "order_reference",
                schema: "accounting",
                table: "invoice");
        }
    }
}
