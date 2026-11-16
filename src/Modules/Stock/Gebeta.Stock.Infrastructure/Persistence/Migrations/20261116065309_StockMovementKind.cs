using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Stock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockMovementKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "stock",
                table: "stock_ledger_entry",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // Rows posted before this step: the kind follows from the quantity's sign and the entry's purpose.
            migrationBuilder.Sql("""
                UPDATE stock.stock_ledger_entry l
                SET kind = CASE
                    WHEN l.actual_qty < 0 AND e.purpose = 'Issue' THEN 'Issue'
                    WHEN l.actual_qty < 0 THEN 'TransferOut'
                    WHEN e.purpose = 'Receipt' THEN 'Receipt'
                    ELSE 'TransferIn' END
                FROM stock.stock_entry e
                WHERE e.id = l.voucher_id AND l.kind = '';
                """);

            migrationBuilder.CreateIndex(
                name: "ix_stock_ledger_entry_item_id_warehouse_id_posting_date_sequen",
                schema: "stock",
                table: "stock_ledger_entry",
                columns: new[] { "item_id", "warehouse_id", "posting_date", "sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stock_ledger_entry_item_id_warehouse_id_posting_date_sequen",
                schema: "stock",
                table: "stock_ledger_entry");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "stock",
                table: "stock_ledger_entry");
        }
    }
}
