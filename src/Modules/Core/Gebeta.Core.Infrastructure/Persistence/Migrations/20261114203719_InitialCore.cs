using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.CreateTable(
                name: "company",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    abbreviation = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    legal_form = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tin = table.Column<string>(type: "character(10)", fixedLength: true, maxLength: 10, nullable: false),
                    vat_registration_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    trade_license_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    default_currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    fiscal_year_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    modified_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_company_abbreviation",
                schema: "core",
                table: "company",
                column: "abbreviation",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_company_tin",
                schema: "core",
                table: "company",
                column: "tin",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company",
                schema: "core");
        }
    }
}
