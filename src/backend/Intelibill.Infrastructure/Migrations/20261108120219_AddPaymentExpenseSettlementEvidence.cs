using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentExpenseSettlementEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "settlement_evidence",
                table: "payment_reversals",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "settlement_evidence",
                table: "expenses",
                type: "jsonb",
                nullable: true);
            // Facts remain nullable for existing rows; never backfill cash or a settlement disposition.
            migrationBuilder.Sql("""
                CREATE FUNCTION protect_source_settlement_evidence() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF OLD.settlement_evidence IS NOT NULL THEN
                            RAISE EXCEPTION 'Source settlement evidence must be retained' USING ERRCODE = '23514';
                        END IF;
                        RETURN OLD;
                    END IF;
                    IF OLD.settlement_evidence IS DISTINCT FROM NEW.settlement_evidence THEN
                        RAISE EXCEPTION 'Source settlement evidence is immutable' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER expenses_settlement_evidence_immutable BEFORE UPDATE OR DELETE ON expenses
                    FOR EACH ROW EXECUTE FUNCTION protect_source_settlement_evidence();
                CREATE TRIGGER payment_reversals_settlement_evidence_immutable BEFORE UPDATE OR DELETE ON payment_reversals
                    FOR EACH ROW EXECUTE FUNCTION protect_source_settlement_evidence();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new System.NotSupportedException("Retain till schema and immutable settlement facts; disable new postings instead of rolling schema back.");
    }
}
