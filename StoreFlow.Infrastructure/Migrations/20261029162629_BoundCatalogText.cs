using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BoundCatalogText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_DescriptionLength",
                table: "Products",
                sql: "char_length(\"Description\") <= 4000");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_NameLength",
                table: "Products",
                sql: "char_length(\"Name\") <= 200");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Categories_DescriptionLength",
                table: "Categories",
                sql: "char_length(\"Description\") <= 4000");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Categories_NameLength",
                table: "Categories",
                sql: "char_length(\"Name\") <= 200");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_DescriptionLength",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_NameLength",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Categories_DescriptionLength",
                table: "Categories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Categories_NameLength",
                table: "Categories");
        }
    }
}
