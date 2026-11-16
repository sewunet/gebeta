using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Stock.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockEntryParty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "party_id",
                schema: "stock",
                table: "stock_entry",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "party_name",
                schema: "stock",
                table: "stock_entry",
                type: "character varying(140)",
                maxLength: 140,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reference",
                schema: "stock",
                table: "stock_entry",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_entry_party_id",
                schema: "stock",
                table: "stock_entry",
                column: "party_id",
                filter: "party_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_stock_entry_party_id",
                schema: "stock",
                table: "stock_entry");

            migrationBuilder.DropColumn(
                name: "party_id",
                schema: "stock",
                table: "stock_entry");

            migrationBuilder.DropColumn(
                name: "party_name",
                schema: "stock",
                table: "stock_entry");

            migrationBuilder.DropColumn(
                name: "reference",
                schema: "stock",
                table: "stock_entry");
        }
    }
}
