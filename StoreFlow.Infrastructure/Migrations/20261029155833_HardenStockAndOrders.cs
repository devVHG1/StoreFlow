using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoreFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenStockAndOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Products",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "Reference",
                table: "Orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "ReservationExpiresAtUtc",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActorId = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Fingerprint = table.Column<string>(type: "text", nullable: false),
                    OrderId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderRequests_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderStatusEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    PreviousStatus = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ActorId = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderStatusEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    ProductName = table.Column<string>(type: "text", nullable: false),
                    QuantityChange = table.Column<long>(type: "bigint", nullable: false),
                    BeforeQuantity = table.Column<int>(type: "integer", nullable: false),
                    AfterQuantity = table.Column<int>(type: "integer", nullable: false),
                    ActorId = table.Column<int>(type: "integer", nullable: true),
                    OrderReference = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status_ReservationExpiresAtUtc",
                table: "Orders",
                columns: new[] { "Status", "ReservationExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRequests_ActorId_Key",
                table: "OrderRequests",
                columns: new[] { "ActorId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderRequests_OrderId",
                table: "OrderRequests",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusEvents_OrderId_Id",
                table: "OrderStatusEvents",
                columns: new[] { "OrderId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductId_Id",
                table: "StockMovements",
                columns: new[] { "ProductId", "Id" });
            migrationBuilder.Sql("""
                UPDATE "Orders" SET "Reference" = gen_random_uuid();
                UPDATE "Orders" SET "ReservationExpiresAtUtc" = CURRENT_TIMESTAMP + interval '30 minutes' WHERE "Status"=1;
                INSERT INTO "StockMovements" ("ProductId","ProductName","QuantityChange","BeforeQuantity","AfterQuantity","Reason","OccurredAtUtc")
                SELECT "Id","Name",0,"StockQuantity","StockQuantity",'LegacyBalance',CURRENT_TIMESTAMP FROM "Products";
                INSERT INTO "OrderStatusEvents" ("OrderId","Status","Reason","OccurredAtUtc")
                SELECT "Id","Status",'LegacySnapshot',CURRENT_TIMESTAMP FROM "Orders";
                
                CREATE FUNCTION storeflow_stock_audit() RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE
                    old_quantity integer := 0;
                    new_quantity integer := 0;
                    product_id integer;
                    product_name text;
                BEGIN
                    IF TG_OP <> 'INSERT' THEN old_quantity := OLD."StockQuantity"; END IF;
                    IF TG_OP <> 'DELETE' THEN new_quantity := NEW."StockQuantity"; END IF;
                    IF TG_OP = 'UPDATE' AND old_quantity = new_quantity THEN RETURN NEW; END IF;
                    IF TG_OP = 'DELETE' THEN product_id := OLD."Id"; product_name := OLD."Name";
                    ELSE product_id := NEW."Id"; product_name := NEW."Name"; END IF;
                    INSERT INTO "StockMovements" ("ProductId","ProductName","QuantityChange","BeforeQuantity","AfterQuantity","ActorId","OrderReference","Reason","OccurredAtUtc")
                    VALUES (product_id, product_name, new_quantity::bigint-old_quantity::bigint, old_quantity, new_quantity,
                        NULLIF(current_setting('storeflow.actor',true),'')::integer,
                        NULLIF(current_setting('storeflow.order',true),'')::uuid,
                        COALESCE(NULLIF(current_setting('storeflow.reason',true),''),'DirectDatabaseWrite') || ':' || TG_OP, clock_timestamp());
                    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                END $fn$;
                CREATE TRIGGER stock_audit AFTER INSERT OR UPDATE OR DELETE ON "Products"
                FOR EACH ROW EXECUTE FUNCTION storeflow_stock_audit();
                
                CREATE FUNCTION storeflow_status_audit() RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE old_status integer;
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        IF OLD."Status" = NEW."Status" AND OLD."IsDeleted" = NEW."IsDeleted" THEN RETURN NEW; END IF;
                        old_status := OLD."Status";
                    END IF;
                    INSERT INTO "OrderStatusEvents" ("OrderId","PreviousStatus","Status","ActorId","Reason","OccurredAtUtc")
                    VALUES (NEW."Id",old_status,NEW."Status",NULLIF(current_setting('storeflow.actor',true),'')::integer,
                        COALESCE(NULLIF(current_setting('storeflow.reason',true),''),'DirectDatabaseWrite'),clock_timestamp());
                    RETURN NEW;
                END $fn$;
                CREATE TRIGGER status_audit AFTER INSERT OR UPDATE ON "Orders"
                FOR EACH ROW EXECUTE FUNCTION storeflow_status_audit();
                
                CREATE FUNCTION storeflow_audit_immutable() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN RAISE EXCEPTION 'Audit records are append-only' USING ERRCODE = '23514'; END $fn$;
                CREATE TRIGGER immutable_stock BEFORE UPDATE OR DELETE ON "StockMovements"
                FOR EACH ROW EXECUTE FUNCTION storeflow_audit_immutable();
                CREATE TRIGGER immutable_status BEFORE UPDATE OR DELETE ON "OrderStatusEvents"
                FOR EACH ROW EXECUTE FUNCTION storeflow_audit_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS stock_audit ON "Products";
                DROP TRIGGER IF EXISTS status_audit ON "Orders";
                DROP FUNCTION IF EXISTS storeflow_stock_audit();
                DROP FUNCTION IF EXISTS storeflow_status_audit();
                DROP TRIGGER IF EXISTS immutable_stock ON "StockMovements";
                DROP TRIGGER IF EXISTS immutable_status ON "OrderStatusEvents";
                DROP FUNCTION IF EXISTS storeflow_audit_immutable();
                """);
            migrationBuilder.DropTable(
                name: "OrderRequests");

            migrationBuilder.DropTable(
                name: "OrderStatusEvents");

            migrationBuilder.DropTable(
                name: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_Orders_Status_ReservationExpiresAtUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Reference",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ReservationExpiresAtUtc",
                table: "Orders");
        }
    }
}
