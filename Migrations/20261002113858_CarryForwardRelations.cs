using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinansApi.Migrations
{
    /// <inheritdoc />
    public partial class CarryForwardRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "OpeningJournalEntryId",
                table: "CarryForwardRun",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<int>(
                name: "ClosingJournalEntryId",
                table: "CarryForwardRun",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.CreateIndex(
                name: "IX_CarryForwardRun_ClosingJournalEntryId",
                table: "CarryForwardRun",
                column: "ClosingJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_CarryForwardRun_OpeningJournalEntryId",
                table: "CarryForwardRun",
                column: "OpeningJournalEntryId");

            migrationBuilder.AddForeignKey(
                name: "FK_CarryForwardRun_FiscalPeriod_SourcePeriodId",
                table: "CarryForwardRun",
                column: "SourcePeriodId",
                principalTable: "FiscalPeriod",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CarryForwardRun_FiscalPeriod_TargetPeriodId",
                table: "CarryForwardRun",
                column: "TargetPeriodId",
                principalTable: "FiscalPeriod",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CarryForwardRun_JournalEntry_ClosingJournalEntryId",
                table: "CarryForwardRun",
                column: "ClosingJournalEntryId",
                principalTable: "JournalEntry",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CarryForwardRun_JournalEntry_OpeningJournalEntryId",
                table: "CarryForwardRun",
                column: "OpeningJournalEntryId",
                principalTable: "JournalEntry",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CarryForwardRun_FiscalPeriod_SourcePeriodId",
                table: "CarryForwardRun");

            migrationBuilder.DropForeignKey(
                name: "FK_CarryForwardRun_FiscalPeriod_TargetPeriodId",
                table: "CarryForwardRun");

            migrationBuilder.DropForeignKey(
                name: "FK_CarryForwardRun_JournalEntry_ClosingJournalEntryId",
                table: "CarryForwardRun");

            migrationBuilder.DropForeignKey(
                name: "FK_CarryForwardRun_JournalEntry_OpeningJournalEntryId",
                table: "CarryForwardRun");

            migrationBuilder.DropIndex(
                name: "IX_CarryForwardRun_ClosingJournalEntryId",
                table: "CarryForwardRun");

            migrationBuilder.DropIndex(
                name: "IX_CarryForwardRun_OpeningJournalEntryId",
                table: "CarryForwardRun");

            migrationBuilder.AlterColumn<int>(
                name: "OpeningJournalEntryId",
                table: "CarryForwardRun",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ClosingJournalEntryId",
                table: "CarryForwardRun",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
