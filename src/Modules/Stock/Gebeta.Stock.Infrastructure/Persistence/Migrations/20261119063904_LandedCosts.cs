using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Stock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LandedCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "landed_cost",
                schema: "stock",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    remark = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    charges = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("pk_landed_cost", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_landed_cost_company_id_name",
                schema: "stock",
                table: "landed_cost",
                columns: new[] { "company_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_landed_cost_receipt_id",
                schema: "stock",
                table: "landed_cost",
                column: "receipt_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "landed_cost",
                schema: "stock");
        }
    }
}
