using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260726120000_AddDistributedCacheTable")]
    public partial class AddDistributedCacheTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Column-for-column the DDL that Microsoft.Extensions.Caching.Postgres
            // 1.2.2 issues under CreateIfNotExists. Deployed environments run with
            // that disabled — the runtime identity holds USAGE but not CREATE on
            // the schema — so the table must already exist and must match, or every
            // cache read fails at runtime rather than here.
            //
            // The table is not an EF entity: no DbSet, nothing in the model
            // snapshot. It is owned by the caching library, and shaping it as an
            // entity would invite a later model diff to alter it out from under it.
            // UNLOGGED matches the library's own default (UseWAL off) and the table
            // dev machines already have. Cache rows are disposable, so skipping WAL
            // is the point; the cost is that the table comes back empty after a
            // crash or a point-in-time restore, which is the correct trade for a
            // cache and is why nothing durable may be stored here.
            migrationBuilder.Sql("""
                CREATE UNLOGGED TABLE IF NOT EXISTS public.cache_entries (
                    id VARCHAR(449) COLLATE "C" PRIMARY KEY,
                    value BYTEA NOT NULL,
                    expiresattime TIMESTAMPTZ NOT NULL,
                    slidingexpirationinseconds BIGINT NULL,
                    absoluteexpiration TIMESTAMPTZ NULL
                );
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_expiresattime
                    ON public.cache_entries (expiresattime)
                    WITH (deduplicate_items=True);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.cache_entries;");
        }
    }
}
