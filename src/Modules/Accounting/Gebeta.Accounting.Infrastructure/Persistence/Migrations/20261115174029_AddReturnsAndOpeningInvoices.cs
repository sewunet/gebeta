using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Accounting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnsAndOpeningInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_opening",
                schema: "accounting",
                table: "invoice",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_return",
                schema: "accounting",
                table: "invoice",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "return_against_id",
                schema: "accounting",
                table: "invoice",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "return_against_number",
                schema: "accounting",
                table: "invoice",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_return_against_id",
                schema: "accounting",
                table: "invoice",
                column: "return_against_id",
                filter: "return_against_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invoice_return_against_id",
                schema: "accounting",
                table: "invoice");

            migrationBuilder.DropColumn(
                name: "is_opening",
                schema: "accounting",
                table: "invoice");

            migrationBuilder.DropColumn(
                name: "is_return",
                schema: "accounting",
                table: "invoice");

            migrationBuilder.DropColumn(
                name: "return_against_id",
                schema: "accounting",
                table: "invoice");

            migrationBuilder.DropColumn(
                name: "return_against_number",
                schema: "accounting",
                table: "invoice");
        }
    }
}
