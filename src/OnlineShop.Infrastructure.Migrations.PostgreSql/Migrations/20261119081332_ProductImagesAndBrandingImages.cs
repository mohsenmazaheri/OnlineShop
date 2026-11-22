using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Infrastructure.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ProductImagesAndBrandingImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "hero_image_url",
                table: "store_branding",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logo_url",
                table: "store_branding",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "alt",
                table: "product_images",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "height",
                table: "product_images",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "width",
                table: "product_images",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "product_image_alt_translations",
                columns: table => new
                {
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    product_image_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alt = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_image_alt_translations", x => new { x.product_image_id, x.locale });
                    table.ForeignKey(
                        name: "fk_product_image_alt_translations_product_images_product_image",
                        column: x => x.product_image_id,
                        principalTable: "product_images",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_image_alt_translations");

            migrationBuilder.DropColumn(
                name: "hero_image_url",
                table: "store_branding");

            migrationBuilder.DropColumn(
                name: "logo_url",
                table: "store_branding");

            migrationBuilder.DropColumn(
                name: "alt",
                table: "product_images");

            migrationBuilder.DropColumn(
                name: "height",
                table: "product_images");

            migrationBuilder.DropColumn(
                name: "width",
                table: "product_images");
        }
    }
}
