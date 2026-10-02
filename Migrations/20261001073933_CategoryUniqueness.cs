using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinansApi.Migrations
{
    /// <inheritdoc />
    public partial class CategoryUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Categories_CompanyId",
                table: "Categories");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_CompanyId_Name_Type",
                table: "Categories",
                columns: new[] { "CompanyId", "Name", "Type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Categories_CompanyId_Name_Type",
                table: "Categories");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_CompanyId",
                table: "Categories",
                column: "CompanyId");
        }
    }
}
