using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartInventory.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Restaurants_ManagerId",
                table: "Restaurants",
                column: "ManagerId");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_OwnerId",
                table: "Organizations",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Restaurants_ManagerId",
                table: "Restaurants");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_OwnerId",
                table: "Organizations");
        }
    }
}
