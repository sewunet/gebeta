using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Buying.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BlanketAgreements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "agreement_id",
                schema: "buying",
                table: "purchase_order",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "blanket_agreement",
                schema: "buying",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    valid_from = table.Column<DateOnly>(type: "date", nullable: false),
                    valid_to = table.Column<DateOnly>(type: "date", nullable: false),
                    amount_limit = table.Column<decimal>(type: "numeric(21,6)", precision: 21, scale: 6, nullable: true),
                    remark = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("pk_blanket_agreement", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_agreement_id",
                schema: "buying",
                table: "purchase_order",
                column: "agreement_id");

            migrationBuilder.CreateIndex(
                name: "ix_blanket_agreement_company_id_name",
                schema: "buying",
                table: "blanket_agreement",
                columns: new[] { "company_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_blanket_agreement_company_id_posting_date",
                schema: "buying",
                table: "blanket_agreement",
                columns: new[] { "company_id", "posting_date" });

            migrationBuilder.CreateIndex(
                name: "ix_blanket_agreement_supplier_id",
                schema: "buying",
                table: "blanket_agreement",
                column: "supplier_id");

            migrationBuilder.AddForeignKey(
                name: "fk_purchase_order_blanket_agreement_agreement_id",
                schema: "buying",
                table: "purchase_order",
                column: "agreement_id",
                principalSchema: "buying",
                principalTable: "blanket_agreement",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_purchase_order_blanket_agreement_agreement_id",
                schema: "buying",
                table: "purchase_order");

            migrationBuilder.DropTable(
                name: "blanket_agreement",
                schema: "buying");

            migrationBuilder.DropIndex(
                name: "ix_purchase_order_agreement_id",
                schema: "buying",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "agreement_id",
                schema: "buying",
                table: "purchase_order");
        }
    }
}
