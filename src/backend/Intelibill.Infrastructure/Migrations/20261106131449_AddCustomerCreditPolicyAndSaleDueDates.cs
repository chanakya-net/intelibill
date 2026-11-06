using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerCreditPolicyAndSaleDueDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "credit_override_by_user_id",
                table: "sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "credit_override_limit",
                table: "sales",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "credit_override_observed_exposure",
                table: "sales",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "credit_override_reason",
                table: "sales",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "due_date",
                table: "sales",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "credit_policy_mode",
                table: "customers",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "credit_terms_days",
                table: "customers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_customers_credit_policy_mode",
                table: "customers",
                sql: "credit_policy_mode IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customers_credit_terms_days",
                table: "customers",
                sql: "credit_terms_days IS NULL OR credit_terms_days BETWEEN 0 AND 365");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_customers_credit_policy_mode",
                table: "customers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customers_credit_terms_days",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "credit_override_by_user_id",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "credit_override_limit",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "credit_override_observed_exposure",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "credit_override_reason",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "due_date",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "credit_policy_mode",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "credit_terms_days",
                table: "customers");
        }
    }
}
