using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Infrastructure.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class GuestCartsAndVariantLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_cart_items_cart_id_product_id",
                table: "cart_items");

            migrationBuilder.AlterColumn<string>(
                name: "customer_id",
                table: "carts",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "guest_token_hash",
                table: "carts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "item_id",
                table: "cart_items",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "variant_id",
                table: "cart_items",
                type: "uuid",
                nullable: true);

            // Lines from before variants were possible are lines of the product itself.
            migrationBuilder.Sql("UPDATE cart_items SET item_id = product_id;");

            migrationBuilder.CreateIndex(
                name: "ix_carts_guest_token_hash",
                table: "carts",
                column: "guest_token_hash",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_cart_items_cart_id_item_id",
                table: "cart_items",
                columns: new[] { "cart_id", "item_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_cart_items_variant_id",
                table: "cart_items",
                column: "variant_id");

            migrationBuilder.AddForeignKey(
                name: "fk_cart_items_product_variants_variant_id",
                table: "cart_items",
                column: "variant_id",
                principalTable: "product_variants",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cart_items_product_variants_variant_id",
                table: "cart_items");

            migrationBuilder.DropIndex(
                name: "ix_carts_guest_token_hash",
                table: "carts");

            migrationBuilder.DropIndex(
                name: "ix_cart_items_cart_id_item_id",
                table: "cart_items");

            migrationBuilder.DropIndex(
                name: "ix_cart_items_variant_id",
                table: "cart_items");

            migrationBuilder.DropColumn(
                name: "guest_token_hash",
                table: "carts");

            migrationBuilder.DropColumn(
                name: "item_id",
                table: "cart_items");

            migrationBuilder.DropColumn(
                name: "variant_id",
                table: "cart_items");

            migrationBuilder.AlterColumn<string>(
                name: "customer_id",
                table: "carts",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(450)",
                oldMaxLength: 450,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_cart_items_cart_id_product_id",
                table: "cart_items",
                columns: new[] { "cart_id", "product_id" },
                unique: true,
                filter: "is_deleted = false");
        }
    }
}
