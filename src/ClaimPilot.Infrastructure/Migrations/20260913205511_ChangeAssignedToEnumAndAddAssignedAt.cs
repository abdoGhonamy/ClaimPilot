using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChangeAssignedToEnumAndAddAssignedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "ApprovalItems" SET "AssignedTo" = NULL WHERE "AssignedTo" = 'pool';
                UPDATE "ApprovalItems" SET "AssignedTo" = 'Adjuster' WHERE "AssignedTo" = 'adjuster';
                UPDATE "ApprovalItems" SET "AssignedTo" = 'Supervisor' WHERE "AssignedTo" = 'supervisor';
                UPDATE "ApprovalItems" SET "AssignedTo" = 'Director' WHERE "AssignedTo" = 'director';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "AssignedTo",
                table: "ApprovalItems",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAt",
                table: "ApprovalItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "ApprovalItems" SET "AssignedAt" = "CreatedAt" WHERE "AssignedTo" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "ApprovalItems");

            migrationBuilder.AlterColumn<string>(
                name: "AssignedTo",
                table: "ApprovalItems",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);
        }
    }
}
