using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoicing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invoice",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    party_tin = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    supplier_invoice_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    net_total = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    withholding_amount = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    outstanding = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    lines = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    doc_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amended_from_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    bank_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posting_date = table.Column<DateOnly>(type: "date", nullable: false),
                    doc_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amended_from_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_company_id_kind_posting_date",
                schema: "accounting",
                table: "invoice",
                columns: new[] { "company_id", "kind", "posting_date" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_company_id_name",
                schema: "accounting",
                table: "invoice",
                columns: new[] { "company_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_company_id_name",
                schema: "accounting",
                table: "payment",
                columns: new[] { "company_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_invoice_id",
                schema: "accounting",
                table: "payment",
                column: "invoice_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "payment",
                schema: "accounting");
        }
    }
}
