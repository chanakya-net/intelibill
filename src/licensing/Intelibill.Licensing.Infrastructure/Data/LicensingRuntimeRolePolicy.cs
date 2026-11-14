namespace Intelibill.Licensing.Infrastructure.Data;

internal static class LicensingRuntimeRolePolicy
{
    // SET follows the membership options introduced in PostgreSQL 16. For
    // every identity we can assume, the privilege functions include its
    // inherited grants, and pg_shdepend finds its inherited ownership.
    // Predefined roles can grant server-wide capabilities. pg_database_owner
    // is handled by the explicit check for ownership of this database.
    internal const string Query = """
        WITH assumable_roles AS MATERIALIZED (
            SELECT oid, rolsuper, rolbypassrls, rolcreatedb, rolcreaterole, rolreplication
            FROM pg_catalog.pg_roles
            WHERE pg_catalog.pg_has_role(session_user, oid, 'SET')
        ), database_identity AS (
            SELECT oid, datdba FROM pg_catalog.pg_database WHERE datname = current_database()
        )
        SELECT EXISTS (SELECT FROM assumable_roles)
            AND NOT EXISTS (
                SELECT FROM assumable_roles r CROSS JOIN database_identity d
                WHERE r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                    OR EXISTS (
                        SELECT FROM pg_catalog.pg_roles predefined
                        WHERE predefined.rolname ~ '^pg_' AND predefined.rolname <> 'pg_database_owner'
                            AND pg_catalog.pg_has_role(r.oid, predefined.oid, 'USAGE')
                    )
                    OR pg_catalog.pg_has_role(r.oid, d.datdba, 'USAGE')
                    OR pg_catalog.has_database_privilege(r.oid, d.oid, 'CREATE, TEMPORARY')
                    OR EXISTS (
                        SELECT FROM pg_catalog.pg_namespace n
                        WHERE pg_catalog.has_schema_privilege(r.oid, n.oid, 'CREATE')
                    )
                    OR EXISTS (
                        SELECT FROM pg_catalog.pg_shdepend dep
                        WHERE dep.dbid = d.oid AND dep.deptype = 'o'
                            AND dep.refclassid = 'pg_catalog.pg_authid'::regclass
                            AND pg_catalog.pg_has_role(r.oid, dep.refobjid, 'USAGE')
                    )
                    OR EXISTS (
                        SELECT FROM pg_catalog.pg_auth_members m
                        WHERE m.member = r.oid AND m.admin_option
                    )
            )
        """;
}
