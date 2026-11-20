using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShopping.Infrastructure.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class StockReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "reserved",
                table: "products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "reserved",
                table: "product_variants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "stock_reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    expires_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    closed_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_reservations", x => x.id);
                    table.CheckConstraint("CK_StockReservations_Quantity", "quantity > 0");
                    table.CheckConstraint("CK_StockReservations_Status", "status IN ('Active', 'Committed', 'Released', 'Expired')");
                    table.ForeignKey(
                        name: "fk_stock_reservations_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_reservations_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_Reserved",
                table: "products",
                sql: "reserved >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductVariants_Reserved",
                table: "product_variants",
                sql: "reserved >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_item_id",
                table: "stock_reservations",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_product_id",
                table: "stock_reservations",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_reference",
                table: "stock_reservations",
                column: "reference");

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_status_expires_utc",
                table: "stock_reservations",
                columns: new[] { "status", "expires_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_reservations_variant_id",
                table: "stock_reservations",
                column: "variant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_reservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Reserved",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductVariants_Reserved",
                table: "product_variants");

            migrationBuilder.DropColumn(
                name: "reserved",
                table: "products");

            migrationBuilder.DropColumn(
                name: "reserved",
                table: "product_variants");
        }
    }
}
