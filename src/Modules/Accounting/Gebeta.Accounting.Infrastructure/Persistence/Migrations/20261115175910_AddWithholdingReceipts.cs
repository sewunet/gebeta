using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWithholdingReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "withholding_base",
                schema: "accounting",
                table: "invoice",
                type: "numeric(21,2)",
                precision: 21,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "withholding_rate_percent",
                schema: "accounting",
                table: "invoice",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "withholding_receipt_number",
                schema: "accounting",
                table: "invoice",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            // Invoices made before this step: withholding was always 2% (the only rate so far), so the base is amount / 2%.
            migrationBuilder.Sql(
                "UPDATE accounting.invoice SET withholding_rate_percent = 2, withholding_base = round(withholding_amount / 0.02, 2) WHERE withholding_amount > 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "withholding_base",
                schema: "accounting",
                table: "invoice");

            migrationBuilder.DropColumn(
                name: "withholding_rate_percent",
                schema: "accounting",
                table: "invoice");

            migrationBuilder.DropColumn(
                name: "withholding_receipt_number",
                schema: "accounting",
                table: "invoice");
        }
    }
}
