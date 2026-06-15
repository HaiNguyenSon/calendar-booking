using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CalendarBooking.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPublicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicId",
                table: "AspNetUsers",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            // Backfill existing users with a unique 10-char code BEFORE the unique index is
            // created (otherwise the empty defaults would collide). New users get a Crockford
            // Base32 code from PublicCode; existing rows get a uuid-derived hex code here —
            // both are valid unique 10-char identifiers.
            migrationBuilder.Sql(
                "UPDATE \"AspNetUsers\" " +
                "SET \"PublicId\" = upper(substr(replace(gen_random_uuid()::text, '-', ''), 1, 10)) " +
                "WHERE \"PublicId\" = '';");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_PublicId",
                table: "AspNetUsers",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_PublicId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "AspNetUsers");
        }
    }
}
