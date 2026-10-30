using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PreserveOrderItemNamesAndDataRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsNameBackfilled",
                table: "OrderItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ProductName",
                table: "OrderItems",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_PricePositive",
                table: "Products",
                sql: "\"Price\" > 0 AND \"Price\" NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_StockNonnegative",
                table: "Products",
                sql: "\"StockQuantity\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_TotalNonnegative",
                table: "Orders",
                sql: "\"TotalAmount\" >= 0 AND \"TotalAmount\" NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderItems_QuantityPositive",
                table: "OrderItems",
                sql: "\"Quantity\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderItems_UnitPricePositive",
                table: "OrderItems",
                sql: "\"UnitPrice\" > 0 AND \"UnitPrice\" NOT IN ('NaN'::numeric, 'Infinity'::numeric, '-Infinity'::numeric)");
            migrationBuilder.Sql("""
                UPDATE "OrderItems" AS item SET "ProductName"=product."Name", "IsNameBackfilled"=true
                FROM "Products" AS product WHERE item."ProductId"=product."Id";

                CREATE FUNCTION storeflow_item_snapshot() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF TG_OP = 'INSERT' THEN
                        IF NEW."ProductName" = '' THEN
                            SELECT "Name" INTO NEW."ProductName" FROM "Products" WHERE "Id"=NEW."ProductId";
                            NEW."ProductName" := COALESCE(NEW."ProductName", 'Товар #' || NEW."ProductId");
                            NEW."IsNameBackfilled" := true;
                        END IF;
                    ELSIF NEW."ProductName" IS DISTINCT FROM OLD."ProductName"
                       OR NEW."IsNameBackfilled" IS DISTINCT FROM OLD."IsNameBackfilled"
                       OR NEW."UnitPrice" IS DISTINCT FROM OLD."UnitPrice"
                       OR NEW."ProductId" IS DISTINCT FROM OLD."ProductId"
                       OR NEW."OrderId" IS DISTINCT FROM OLD."OrderId" THEN
                        RAISE EXCEPTION 'Order item snapshot is immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $fn$;
                CREATE TRIGGER item_snapshot BEFORE INSERT OR UPDATE ON "OrderItems"
                FOR EACH ROW EXECUTE FUNCTION storeflow_item_snapshot();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS item_snapshot ON "OrderItems";
                DROP FUNCTION IF EXISTS storeflow_item_snapshot();
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_PricePositive",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_StockNonnegative",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_TotalNonnegative",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderItems_QuantityPositive",
                table: "OrderItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderItems_UnitPricePositive",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "IsNameBackfilled",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "ProductName",
                table: "OrderItems");
        }
    }
}
