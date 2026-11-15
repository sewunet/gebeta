using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodClosing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "period_closing",
                schema: "accounting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    start = table.Column<DateOnly>(type: "date", nullable: false),
                    end = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    total_debit = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    total_credit = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    net_profit = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    reopened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reopened_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    reopen_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_period_closing", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_period_closing_company_id_end",
                schema: "accounting",
                table: "period_closing",
                columns: new[] { "company_id", "end" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "period_closing",
                schema: "accounting");
        }
    }
}
