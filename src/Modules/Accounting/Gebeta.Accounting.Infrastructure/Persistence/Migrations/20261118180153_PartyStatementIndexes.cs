using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PartyStatementIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_payment_company_id_party_id_posting_date",
                schema: "accounting",
                table: "payment",
                columns: new[] { "company_id", "party_id", "posting_date" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_company_id_party_id_posting_date",
                schema: "accounting",
                table: "invoice",
                columns: new[] { "company_id", "party_id", "posting_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payment_company_id_party_id_posting_date",
                schema: "accounting",
                table: "payment");

            migrationBuilder.DropIndex(
                name: "ix_invoice_company_id_party_id_posting_date",
                schema: "accounting",
                table: "invoice");
        }
    }
}
