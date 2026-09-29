using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using BSStore.Infrastructure.Data;

#nullable disable

namespace BSStore.Infrastructure.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260929120000_AddCustomerOrderReviews")]
public partial class AddCustomerOrderReviews : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "CustomerRating", table: "Orders", type: "integer", nullable: true);
        migrationBuilder.AddColumn<string>(name: "CustomerReview", table: "Orders", type: "text", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "CustomerRatedAt", table: "Orders", type: "timestamp with time zone", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CustomerRating", table: "Orders");
        migrationBuilder.DropColumn(name: "CustomerReview", table: "Orders");
        migrationBuilder.DropColumn(name: "CustomerRatedAt", table: "Orders");
    }
}
