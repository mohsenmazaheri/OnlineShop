using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Infrastructure.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ProductVideos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_videos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    poster_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    duration_seconds = table.Column<double>(type: "double precision", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_videos", x => x.id);
                    table.ForeignKey(
                        name: "fk_product_videos_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_video_title_translations",
                columns: table => new
                {
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    product_video_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_video_title_translations", x => new { x.product_video_id, x.locale });
                    table.ForeignKey(
                        name: "fk_product_video_title_translations_product_videos_product_vid",
                        column: x => x.product_video_id,
                        principalTable: "product_videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_videos_product_id",
                table: "product_videos",
                column: "product_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_video_title_translations");

            migrationBuilder.DropTable(
                name: "product_videos");
        }
    }
}
