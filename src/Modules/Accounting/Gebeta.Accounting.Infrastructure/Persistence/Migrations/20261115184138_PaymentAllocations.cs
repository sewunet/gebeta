using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Gebeta.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_allocation",
                schema: "accounting",
                columns: table => new
                {
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(21,2)", precision: 21, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_allocation", x => new { x.payment_id, x.id });
                    table.ForeignKey(
                        name: "fk_payment_allocation_payment_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "accounting",
                        principalTable: "payment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocation_invoice_id",
                schema: "accounting",
                table: "payment_allocation",
                column: "invoice_id");

            // Each existing payment settled exactly one invoice: it becomes one allocation.
            migrationBuilder.Sql(
                "INSERT INTO accounting.payment_allocation (payment_id, invoice_id, invoice_number, amount) SELECT id, invoice_id, invoice_number, amount FROM accounting.payment;");

            migrationBuilder.DropIndex(
                name: "ix_payment_invoice_id",
                schema: "accounting",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "invoice_id",
                schema: "accounting",
                table: "payment");

            migrationBuilder.DropColumn(
                name: "invoice_number",
                schema: "accounting",
                table: "payment");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_allocation",
                schema: "accounting");

            migrationBuilder.AddColumn<Guid>(
                name: "invoice_id",
                schema: "accounting",
                table: "payment",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "invoice_number",
                schema: "accounting",
                table: "payment",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_invoice_id",
                schema: "accounting",
                table: "payment",
                column: "invoice_id");
        }
    }
}
