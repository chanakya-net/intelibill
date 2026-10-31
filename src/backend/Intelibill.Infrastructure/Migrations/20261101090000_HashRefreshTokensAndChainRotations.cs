using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <summary>
    /// Expand step of hash-only refresh credentials. Adds the hash, the rotation chain and the
    /// consumption and revocation-reason columns, and ends every credential written in the old
    /// raw-token format, along with the sessions they renewed: everyone signs in once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Expand/contract: the previous revision keeps serving until this job succeeds, and it still
    /// reads and writes <c>token</c> and <c>is_revoked</c>. So <c>token</c> is kept, made nullable
    /// and emptied rather than dropped, and <c>token_hash</c> stays nullable. The contract migration
    /// removes rows with no hash, drops <c>token</c> with its index and makes <c>token_hash</c>
    /// required.
    /// </para>
    /// <para>
    /// The old credentials are revoked rather than carried over even though their hashes are
    /// computed here: every raw value was stored in plain text, including in backups, so none of
    /// them can be trusted from here on. The hash is kept so a leaked value can still be matched to
    /// its row; the raw value itself is not.
    /// </para>
    /// </remarks>
    public partial class HashRefreshTokensAndChainRotations : Migration
    {
        private const string CutoverReason = "credential-format-cutover";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "consumed_at",
                table: "refresh_tokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_token_id",
                table: "refresh_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "revocation_reason",
                table: "refresh_tokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "token_hash",
                table: "refresh_tokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // The previous revision still inserts raw values; the new one never writes this column.
            migrationBuilder.AlterColumn<string>(
                name: "token",
                table: "refresh_tokens",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.Sql(
                $"""
                UPDATE refresh_tokens
                SET token_hash = upper(encode(sha256(convert_to(token, 'UTF8')), 'hex')),
                    token = NULL,
                    is_revoked = TRUE,
                    revoked_at = COALESCE(revoked_at, now()),
                    revocation_reason = COALESCE(revocation_reason, '{CutoverReason}')
                WHERE token IS NOT NULL;
                """);

            // A session whose credentials were all just revoked can never renew; ending it now keeps
            // the owner's device list honest instead of showing devices nobody can use.
            migrationBuilder.Sql(
                $"""
                UPDATE auth_sessions
                SET revoked_at = now(),
                    revocation_reason = '{CutoverReason}'
                WHERE revoked_at IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_parent_token_id",
                table: "refresh_tokens",
                column: "parent_token_id",
                unique: true,
                filter: "parent_token_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_parent_token_id",
                table: "refresh_tokens");

            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens");

            // Hash-only rows have no raw value to restore, and raw storage is never re-created for
            // them: they are removed, and their holders sign in again.
            migrationBuilder.Sql("DELETE FROM refresh_tokens WHERE token IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "token",
                table: "refresh_tokens",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "consumed_at",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "parent_token_id",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "revocation_reason",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "token_hash",
                table: "refresh_tokens");
        }
    }
}
