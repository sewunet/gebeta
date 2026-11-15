using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBanking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bank_account",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    bank_name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    account_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ledger_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_account_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_account", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bank_match",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    statement_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gl_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_match", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_line",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    balance = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: true),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_statement_line", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_account_company_id",
                schema: "accounting",
                table: "bank_account",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_account_ledger_account_id",
                schema: "accounting",
                table: "bank_account",
                column: "ledger_account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_match_bank_account_id_statement_line_id",
                schema: "accounting",
                table: "bank_match",
                columns: new[] { "bank_account_id", "statement_line_id" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_match_gl_entry_id",
                schema: "accounting",
                table: "bank_match",
                column: "gl_entry_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_statement_line_bank_account_id_date",
                schema: "accounting",
                table: "bank_statement_line",
                columns: new[] { "bank_account_id", "date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_account",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "bank_match",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "bank_statement_line",
                schema: "accounting");
        }
    }
}
