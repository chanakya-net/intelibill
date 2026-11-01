using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261101150000_AddAtomicRateLimitBuckets")]
    public partial class AddAtomicRateLimitBuckets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Read and written only by PostgresRateLimitStore through raw SQL, so it is not an EF
            // entity: no DbSet, nothing in the model snapshot for a later diff to alter.
            //
            // Logged rather than UNLOGGED, unlike cache_entries: a crash or a failover that came
            // back with this table empty would hand every caller a fresh allowance at the moment
            // the service is most likely to be under pressure.
            //
            // Not tenant data. Buckets are keyed on hashed addresses, accounts and destinations,
            // never on a shop, and they are written before a request has any shop context, so the
            // table stays outside row level security alongside the identity tables. The runtime
            // role reaches it through the schema's default DML grant; it holds nothing a caller
            // could read back as an identifier.
            migrationBuilder.Sql("""
                CREATE TABLE rate_limit_buckets (
                    policy_id      character varying(200) NOT NULL,
                    bucket_key     character varying(128) NOT NULL,
                    window_start   timestamp with time zone NOT NULL,
                    window_seconds integer NOT NULL,
                    count          integer NOT NULL,
                    blocked_until  timestamp with time zone NULL,
                    expires_at     timestamp with time zone NOT NULL,
                    CONSTRAINT pk_rate_limit_buckets PRIMARY KEY (policy_id, bucket_key),
                    CONSTRAINT ck_rate_limit_buckets_count CHECK (count >= 0),
                    CONSTRAINT ck_rate_limit_buckets_window_seconds CHECK (window_seconds > 0)
                );
                """);

            migrationBuilder.Sql("""
                CREATE INDEX ix_rate_limit_buckets_expires_at ON rate_limit_buckets (expires_at);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS rate_limit_buckets;");
        }
    }
}
