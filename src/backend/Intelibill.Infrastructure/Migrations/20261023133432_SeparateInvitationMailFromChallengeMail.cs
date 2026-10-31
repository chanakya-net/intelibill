using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeparateInvitationMailFromChallengeMail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "invitation_id",
                table: "identity_mail",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_identity_mail_invitation_id",
                table: "identity_mail",
                column: "invitation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_identity_mail_invitation_id",
                table: "identity_mail");

            migrationBuilder.DropColumn(
                name: "invitation_id",
                table: "identity_mail");
        }
    }
}
