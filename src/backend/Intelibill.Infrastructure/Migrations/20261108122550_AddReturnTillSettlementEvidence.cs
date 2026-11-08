using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnTillSettlementEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "refund_confirmed",
                table: "sale_returns",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_tender",
                table: "sale_returns",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "void_settlement_evidence",
                table: "sale_returns",
                type: "jsonb",
                nullable: true);
            migrationBuilder.Sql("""
                CREATE FUNCTION protect_return_settlement_evidence() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF OLD.refund_confirmed IS NOT NULL OR OLD.void_settlement_evidence IS NOT NULL THEN
                            RAISE EXCEPTION 'Return settlement evidence must be retained' USING ERRCODE = '23514';
                        END IF;
                        RETURN OLD;
                    END IF;
                    IF OLD.refund_confirmed IS DISTINCT FROM NEW.refund_confirmed OR OLD.refund_tender IS DISTINCT FROM NEW.refund_tender OR
                        (OLD.void_settlement_evidence IS NOT NULL AND OLD.void_settlement_evidence IS DISTINCT FROM NEW.void_settlement_evidence) OR
                        (NEW.void_settlement_evidence IS NOT NULL AND (NOT NEW.is_voided OR (OLD.void_settlement_evidence IS NULL AND OLD.is_voided))) THEN
                        RAISE EXCEPTION 'Return settlement evidence is immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER sale_returns_settlement_evidence_immutable BEFORE UPDATE OR DELETE ON sale_returns
                    FOR EACH ROW EXECUTE FUNCTION protect_return_settlement_evidence();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new System.NotSupportedException("Retain till schema and immutable settlement facts; disable new postings instead of rolling schema back.");
    }
}
