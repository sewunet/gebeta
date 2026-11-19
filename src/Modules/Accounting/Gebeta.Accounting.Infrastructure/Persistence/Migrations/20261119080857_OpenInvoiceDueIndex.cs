using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpenInvoiceDueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_invoice_company_id_kind_due_date",
                schema: "accounting",
                table: "invoice",
                columns: new[] { "company_id", "kind", "due_date" },
                filter: "outstanding > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invoice_company_id_kind_due_date",
                schema: "accounting",
                table: "invoice");
        }
    }
}
