using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CalendarBooking.Data.Migrations
{
    /// <inheritdoc />
    public partial class HardenNicknameUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Replace the plain unique index with a case-insensitive one so "Alice" and
            // "alice" can't both be registered. A functional index on LOWER(Nickname) is a
            // Postgres feature EF can't model, so it's created with raw SQL.
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_Nickname",
                table: "AspNetUsers");

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_AspNetUsers_Nickname_Lower\" ON \"AspNetUsers\" (LOWER(\"Nickname\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX \"IX_AspNetUsers_Nickname_Lower\";");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_Nickname",
                table: "AspNetUsers",
                column: "Nickname",
                unique: true);
        }
    }
}
