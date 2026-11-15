using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Cash.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cash");

            migrationBuilder.CreateTable(
                name: "cash_ledger_entry",
                schema: "cash",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cashier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_ledger_entry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cash_settings",
                schema: "cash",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enable_auto_request = table.Column<bool>(type: "boolean", nullable: false),
                    minimum_balance = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    target_balance = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    default_cash_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    default_source_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cashier",
                schema: "cash",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cash_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    auto_top_up = table.Column<bool>(type: "boolean", nullable: false),
                    max_transaction = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: true),
                    max_withdrawal = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: true),
                    max_deposit = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: true),
                    balance = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cashier", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_message",
                schema: "cash",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handler = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_message", x => new { x.message_id, x.handler });
                });

            migrationBuilder.CreateTable(
                name: "naming_series_counter",
                schema: "cash",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    current = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_naming_series_counter", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "cash",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    routing_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    clr_type = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_message", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "petty_cash_request",
                schema: "cash",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cashier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cashier_name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    requested_amount = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    balance_when_requested = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    created_by_system = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    funding_from = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    source_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    funding_cashier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("pk_petty_cash_request", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cash_ledger_entry_cashier_id_date",
                schema: "cash",
                table: "cash_ledger_entry",
                columns: new[] { "cashier_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_settings_company_id",
                schema: "cash",
                table: "cash_settings",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cashier_company_id_code",
                schema: "cash",
                table: "cashier",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                schema: "cash",
                table: "outbox_message",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_petty_cash_request_cashier_id_status",
                schema: "cash",
                table: "petty_cash_request",
                columns: new[] { "cashier_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_petty_cash_request_company_id_name",
                schema: "cash",
                table: "petty_cash_request",
                columns: new[] { "company_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_ledger_entry",
                schema: "cash");

            migrationBuilder.DropTable(
                name: "cash_settings",
                schema: "cash");

            migrationBuilder.DropTable(
                name: "cashier",
                schema: "cash");

            migrationBuilder.DropTable(
                name: "inbox_message",
                schema: "cash");

            migrationBuilder.DropTable(
                name: "naming_series_counter",
                schema: "cash");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "cash");

            migrationBuilder.DropTable(
                name: "petty_cash_request",
                schema: "cash");
        }
    }
}
