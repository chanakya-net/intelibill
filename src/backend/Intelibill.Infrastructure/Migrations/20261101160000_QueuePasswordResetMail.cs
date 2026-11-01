using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QueuePasswordResetMail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "password_reset_token_id",
                table: "identity_mail",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_identity_mail_password_reset_token_id",
                table: "identity_mail",
                column: "password_reset_token_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_identity_mail_password_reset_token_id",
                table: "identity_mail");

            migrationBuilder.DropColumn(
                name: "password_reset_token_id",
                table: "identity_mail");
        }
    }
}
