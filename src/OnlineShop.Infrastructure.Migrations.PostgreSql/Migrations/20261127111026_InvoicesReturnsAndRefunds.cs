using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnlineShop.Infrastructure.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class InvoicesReturnsAndRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "completed_utc",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<long>(type: "bigint", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    seller_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    seller_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    seller_tax_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoices_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "return_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<long>(type: "bigint", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    answer = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_return_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_return_requests_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sales_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    seller_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    seller_tax_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    invoice_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    return_days = table.Column<int>(type: "integer", nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refunds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    actor_user_id = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refunds", x => x.id);
                    table.ForeignKey(
                        name: "fk_refunds_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_refunds_return_requests_return_request_id",
                        column: x => x.return_request_id,
                        principalTable: "return_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "return_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_return_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_return_lines_order_items_order_item_id",
                        column: x => x.order_item_id,
                        principalTable: "order_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_return_lines_return_requests_return_request_id",
                        column: x => x.return_request_id,
                        principalTable: "return_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_number",
                table: "invoices",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_order_id",
                table: "invoices",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refunds_order_id",
                table: "refunds",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_return_request_id",
                table: "refunds",
                column: "return_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_lines_order_item_id",
                table: "return_lines",
                column: "order_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_lines_return_request_id",
                table: "return_lines",
                column: "return_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_return_requests_number",
                table: "return_requests",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_return_requests_order_id",
                table: "return_requests",
                column: "order_id");

            // Orders paid before invoices existed get theirs, numbered in the order they were paid (the seller's name is
            // left empty: the invoice page shows the shop's name). Delivered orders get their delivery time.
            migrationBuilder.Sql("""
                INSERT INTO invoices (id, number, order_id, issued_utc, seller_name, created_utc, is_deleted)
                SELECT gen_random_uuid(), row_number() OVER (ORDER BY paid.at, o.number), o.id, paid.at, '', paid.at, false
                FROM orders o
                CROSS JOIN LATERAL (
                    SELECT COALESCE(MIN(e.created_utc), o.ordered_utc) AS at FROM order_events e
                    WHERE e.order_id = o.id AND e.kind = 'StatusChanged' AND e.status = 'Paid') AS paid
                WHERE o.status IN ('Paid', 'Shipped', 'Completed');

                UPDATE orders o SET completed_utc = done.at
                FROM (SELECT order_id, MIN(created_utc) AS at FROM order_events WHERE kind = 'StatusChanged' AND status = 'Completed' GROUP BY order_id) AS done
                WHERE o.id = done.order_id AND o.status = 'Completed';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "refunds");

            migrationBuilder.DropTable(
                name: "return_lines");

            migrationBuilder.DropTable(
                name: "sales_settings");

            migrationBuilder.DropTable(
                name: "return_requests");

            migrationBuilder.DropColumn(
                name: "completed_utc",
                table: "orders");
        }
    }
}
