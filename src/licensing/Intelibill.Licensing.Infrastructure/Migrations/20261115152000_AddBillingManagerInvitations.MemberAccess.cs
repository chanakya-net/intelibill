namespace Intelibill.Licensing.Infrastructure.Migrations;

public sealed partial class AddBillingManagerInvitations
{
    private const string MemberAccess = """
        -- Keep the prior membership TRUNCATE retention trigger authoritative. A referencing foreign key
        -- would be checked before that trigger. Check the new link at commit in the recipient scope.
        CREATE FUNCTION public.check_billing_invitation_grant() RETURNS trigger
        LANGUAGE plpgsql SET search_path = pg_catalog AS $$
        BEGIN
            IF NOT EXISTS (
                SELECT FROM public.billing_account_members m WHERE m.id = NEW.accepted_member_id
                    AND m.account_id = NEW.account_id AND m.merchant_user_id = NEW.invited_actor_id
                    AND m.role = 2 AND m.status = 1 AND m.revision = 1 AND m.created_at = NEW.accepted_at
            ) THEN
                RAISE EXCEPTION 'Billing invitation grant is missing' USING ERRCODE = '23514';
            END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER billing_invitation_grant_complete
            AFTER UPDATE ON public.billing_account_invitations DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION public.check_billing_invitation_grant();

        -- Only the migration identity behind the scoped function receives this non-recursive policy.
        DO $$
        BEGIN
            EXECUTE format('CREATE POLICY billing_member_function_read ON public.billing_account_members
                FOR SELECT TO %I USING (true)', current_user);
            EXECUTE format('CREATE POLICY billing_member_function_lock ON public.billing_account_members
                FOR UPDATE TO %I USING (true) WITH CHECK (false)', current_user);
        END $$;

        CREATE FUNCTION public.billing_roster(p_after uuid, p_target uuid)
        RETURNS TABLE (member_id uuid, role integer, revision integer, is_current boolean)
        LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog AS $$
            WITH scope AS (
                SELECT nullif(current_setting('app.billing_account_id', true), '')::uuid AS account_id,
                       nullif(current_setting('app.billing_actor_id', true), '')::uuid AS actor_id
            )
            SELECT m.id, m.role, m.revision, m.merchant_user_id = s.actor_id
            FROM public.billing_account_members m CROSS JOIN scope s
            WHERE has_table_privilege(session_user, 'public.billing_account_members', 'SELECT')
              AND m.account_id = s.account_id AND (p_target IS NOT NULL OR m.status = 1) AND m.id > p_after
              AND (p_target IS NULL OR m.merchant_user_id = p_target)
              AND EXISTS (SELECT FROM public.billing_account_members caller
                  WHERE caller.account_id = s.account_id AND caller.merchant_user_id = s.actor_id
                    AND caller.status = 1 AND (p_target IS NULL OR caller.role = 1))
            ORDER BY m.id LIMIT 101
        $$;

        CREATE FUNCTION public.billing_inviter_is_current(p_invitation uuid) RETURNS boolean
        LANGUAGE sql VOLATILE SECURITY DEFINER SET search_path = pg_catalog AS $$
            SELECT EXISTS (
                SELECT FROM public.billing_account_invitations i
                JOIN public.billing_account_members issuer ON issuer.account_id = i.account_id
                  AND issuer.merchant_user_id = i.issuer_actor_id AND issuer.revision = i.issuer_revision
                  AND issuer.role = 1 AND issuer.status = 1
                WHERE has_table_privilege(session_user, 'public.billing_account_members', 'SELECT')
                  AND i.id = p_invitation AND i.revision = 1
                  AND i.account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid
                  AND i.invited_actor_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
                FOR SHARE OF issuer
            )
        $$;

        CREATE POLICY billing_account_members_insert_manager ON public.billing_account_members FOR INSERT WITH CHECK (
            account_id = nullif(current_setting('app.billing_account_id', true), '')::uuid
            AND merchant_user_id = nullif(current_setting('app.billing_actor_id', true), '')::uuid
            AND role = 2 AND status = 1 AND revision = 1
            AND EXISTS (SELECT FROM public.billing_account_invitations i WHERE i.revision = 2
                AND i.account_id = billing_account_members.account_id AND i.invited_actor_id = merchant_user_id
                AND i.accepted_member_id = billing_account_members.id AND i.accepted_at = billing_account_members.created_at)
        );
        """;
}
