using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gebeta.Core.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_change",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    old_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    new_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_change", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_change_entity",
                schema: "core",
                table: "audit_change",
                columns: new[] { "entity_id", "changed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_change",
                schema: "core");
        }
    }
}
