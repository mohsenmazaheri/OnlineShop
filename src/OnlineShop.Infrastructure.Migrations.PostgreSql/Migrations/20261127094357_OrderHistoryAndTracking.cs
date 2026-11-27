using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Infrastructure.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class OrderHistoryAndTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tracking_carrier",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tracking_code",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    actor_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    is_internal = table.Column<bool>(type: "boolean", nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_events_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Orders placed before the history existed start it with their "placed" entry.
            migrationBuilder.Sql("""
                INSERT INTO order_events (id, order_id, kind, is_internal, created_utc, is_deleted)
                SELECT gen_random_uuid(), id, 'Placed', false, ordered_utc, false FROM orders;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_orders_ordered_utc",
                table: "orders",
                column: "ordered_utc");

            migrationBuilder.CreateIndex(
                name: "ix_order_events_order_id_created_utc",
                table: "order_events",
                columns: new[] { "order_id", "created_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_events");

            migrationBuilder.DropIndex(
                name: "ix_orders_ordered_utc",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "tracking_carrier",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "tracking_code",
                table: "orders");
        }
    }
}
