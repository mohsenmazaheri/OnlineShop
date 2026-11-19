using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShopping.Infrastructure.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class StoreBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "store_branding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    primary_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    corners = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    font = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    home_layout = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_branding", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "store_branding");
        }
    }
}
