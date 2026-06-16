using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CalendarBooking.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSlotGeneratedByRuleId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GeneratedByRuleId",
                table: "AvailabilitySlots",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilitySlots_GeneratedByRuleId",
                table: "AvailabilitySlots",
                column: "GeneratedByRuleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AvailabilitySlots_GeneratedByRuleId",
                table: "AvailabilitySlots");

            migrationBuilder.DropColumn(
                name: "GeneratedByRuleId",
                table: "AvailabilitySlots");
        }
    }
}
