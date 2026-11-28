using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Intelibill.Licensing.Integration.Tests;

public partial class LicensingPostgreSqlFixture
{
    private const string OwnerTemplate = "licensing_owner_template";
    private const string TrialTemplate = "licensing_trial_template";

    internal const string SignInRuntimeGrants = """
        REVOKE ALL ON SCHEMA public FROM PUBLIC;
        GRANT USAGE ON SCHEMA public TO licensing_runtime;
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO licensing_runtime;
        GRANT EXECUTE ON FUNCTION public.claim_entitlement_delivery(timestamptz,uuid) TO licensing_runtime;
        GRANT EXECUTE ON FUNCTION public.finish_entitlement_delivery(uuid,uuid,timestamptz,boolean,boolean,text) TO licensing_runtime;
        GRANT EXECUTE ON FUNCTION public.due_entitlement_verification(timestamptz) TO licensing_runtime;
        GRANT EXECUTE ON FUNCTION public.due_transfer_applications(timestamptz) TO licensing_runtime;
        GRANT EXECUTE ON FUNCTION public.due_scheduled_capacity_reductions(timestamptz) TO licensing_runtime;
        GRANT EXECUTE ON FUNCTION public.due_notification_planning(timestamptz,uuid,integer) TO licensing_runtime;
        GRANT EXECUTE ON FUNCTION public.account_notification_billing_members(uuid,uuid,integer) TO licensing_runtime;
        """;

    private static readonly Lazy<Task<SeededProviderTemplate>> SharedOwnerTemplate =
        new(() => PrepareOwnerTemplateAsync(SharedContainer));
    private static readonly Lazy<Task<SeededProviderTemplate>> SharedTrialTemplate =
        new(() => PrepareTrialTemplateAsync(SharedContainer));
    private static readonly ConcurrentDictionary<string, Lazy<Task>> SlotTemplates = new(StringComparer.Ordinal);

    internal async Task<(NpgsqlConnectionStringBuilder Runtime, SeededProviderTemplate Template)> CreateOwnerDatabaseAsync(string name)
    {
        var template = await SharedOwnerTemplate.Value;
        var source = await EnsureSlotTemplateAsync(template.TemplateDatabase);
        await CloneTemplateDatabaseAsync(name, source);
        return (Login(name, RuntimeRole), template);
    }

    internal async Task<(NpgsqlConnectionStringBuilder Runtime, SeededProviderTemplate Template)> CreateTrialDatabaseAsync(string name)
    {
        var template = await SharedTrialTemplate.Value;
        var source = await EnsureSlotTemplateAsync(template.TemplateDatabase);
        await CloneTemplateDatabaseAsync(name, source);
        return (Login(name, RuntimeRole), template);
    }

    private async Task<string> EnsureSlotTemplateAsync(string masterTemplate)
    {
        if (_isolated || _slotIndex < 0)
            return masterTemplate;

        var slotTemplate = $"{masterTemplate}_s{_slotIndex}";
        var lazy = SlotTemplates.GetOrAdd(
            slotTemplate,
            _ => new Lazy<Task>(() => CloneAsSlotTemplateAsync(_container, masterTemplate, slotTemplate)));
        await lazy.Value;
        return slotTemplate;
    }

    private static async Task CloneAsSlotTemplateAsync(
        PostgreSqlContainer container,
        string masterTemplate,
        string slotTemplate)
    {
        var admin = Bootstrap(container, "bootstrap");
        await ExecuteAsync(admin, $"CREATE DATABASE {slotTemplate} OWNER licensing_migrator TEMPLATE {masterTemplate}");
        await ExecuteAsync(admin, $"REVOKE ALL ON DATABASE {slotTemplate} FROM PUBLIC");
        await ExecuteAsync(admin, $"ALTER DATABASE {slotTemplate} WITH ALLOW_CONNECTIONS false IS_TEMPLATE true");
    }

    private async Task CloneTemplateDatabaseAsync(string name, string sourceTemplate)
    {
        var identifier = await CreateDatabaseAsync(name, $"OWNER licensing_migrator TEMPLATE {sourceTemplate}");
        await ExecuteAsync(Bootstrap(), $"""
            REVOKE ALL ON DATABASE {identifier} FROM PUBLIC;
            GRANT CONNECT ON DATABASE {identifier} TO licensing_runtime;
            """);
    }

    private static async Task<SeededProviderTemplate> PrepareOwnerTemplateAsync(PostgreSqlContainer container)
    {
        await SharedTemplate.Value;
        var admin = Bootstrap(container, "bootstrap");
        await ExecuteAsync(admin, $"CREATE DATABASE {OwnerTemplate} OWNER licensing_migrator TEMPLATE {LicensingTemplate}");
        await ExecuteAsync(admin, $"""
            REVOKE ALL ON DATABASE {OwnerTemplate} FROM PUBLIC;
            GRANT CONNECT ON DATABASE {OwnerTemplate} TO licensing_runtime;
            """);

        var migrator = Bootstrap(container, OwnerTemplate);
        migrator.Username = MigratorRole;
        await ExecuteAsync(migrator, SignInRuntimeGrants);

        var runtime = Bootstrap(container, OwnerTemplate);
        runtime.Username = RuntimeRole;
        var mfaKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var clock = new P0106Tests.TestClock();
        var settings = BuildTemplateProviderSettings(mfaKey, signingKey);

        string password;
        string seed;
        await using (var api = new LicensingApiFactory(runtime, clock: clock, settings: settings))
        {
            (password, seed, _) = await P0108Tests.EnrollOwnerWithCodesAsync(api, clock);
        }

        await FreezeTemplateDatabaseAsync(admin, OwnerTemplate);
        return new SeededProviderTemplate(
            OwnerTemplate,
            mfaKey,
            signingKey,
            password,
            seed,
            clock.GetUtcNow());
    }

    private static async Task<SeededProviderTemplate> PrepareTrialTemplateAsync(PostgreSqlContainer container)
    {
        var owner = await SharedOwnerTemplate.Value;
        var admin = Bootstrap(container, "bootstrap");
        await ExecuteAsync(admin, $"CREATE DATABASE {TrialTemplate} OWNER licensing_migrator TEMPLATE {OwnerTemplate}");
        await ExecuteAsync(admin, $"""
            REVOKE ALL ON DATABASE {TrialTemplate} FROM PUBLIC;
            GRANT CONNECT ON DATABASE {TrialTemplate} TO licensing_runtime;
            """);

        var migrator = Bootstrap(container, TrialTemplate);
        migrator.Username = MigratorRole;
        await ExecuteAsync(migrator, P0302Fixture.RuntimeGrant);

        var runtime = Bootstrap(container, TrialTemplate);
        runtime.Username = RuntimeRole;
        var clock = new P0106Tests.TestClock(owner.ReadyClockUtc);
        var mailer = new P0114Tests.FixtureMailer();
        var settings = BuildTemplateProviderSettings(owner.MfaKey, owner.SessionSigningKey);
        foreach (var (key, value) in P0207Tests.LocalSettings())
            settings[key] = value;

        string managerSeed;
        IReadOnlyDictionary<string, string> managerCookies;
        Guid policyId;
        int policyRevision;

        await using (var api = new LicensingApiFactory(
            runtime,
            clock: clock,
            settings: settings,
            configure: services => services.AddSingleton<Application.AdminIdentities.IAdminInvitationMailer>(mailer)))
        {
            clock.Advance(TimeSpan.FromSeconds(30));
            using var ownerBrowser = await P0114Tests.SignInAsync(
                api,
                clock,
                P0112Tests.OwnerEmail,
                owner.OwnerPassword,
                owner.OwnerSeed);

            await P0114Tests.IssueAsync(ownerBrowser, P0114Tests.Invitee);
            using var enrollment = P0114Tests.EnrollmentClient(api);
            using var accepted = await P0206Fixture.PostEnrollmentAsync(
                enrollment,
                "/api/admin/auth/invitation/context",
                "/api/admin/auth/invitation/accept",
                new { invitationToken = mailer.Token, email = P0114Tests.Invitee, password = P0114Tests.InviteePassword });
            Assert.Equal(System.Net.HttpStatusCode.OK, accepted.StatusCode);
            managerSeed = await P0114Tests.CompleteEnrollmentAsync(enrollment, clock);

            clock.Advance(TimeSpan.FromSeconds(30));
            using (var grantor = await P0114Tests.SignInAsync(
                api,
                clock,
                P0112Tests.OwnerEmail,
                owner.OwnerPassword,
                owner.OwnerSeed))
            {
                await P0206Fixture.GrantAsync(grantor, P0114Tests.Invitee, P0206Fixture.Manager);
            }

            clock.Advance(TimeSpan.FromSeconds(30));
            using var managerBrowser = await P0114Tests.SignInAsync(
                api,
                clock,
                P0114Tests.Invitee,
                P0114Tests.InviteePassword,
                managerSeed);

            policyId = await P0207Tests.PublishTrialAsync(managerBrowser, 1);
            policyRevision = (int)(await ReadScalarAsync(
                Bootstrap(container, TrialTemplate),
                $"SELECT revision FROM trial_policy_versions WHERE id = '{policyId:D}'"))!;
            managerCookies = managerBrowser.SnapshotCookies();
        }

        await FreezeTemplateDatabaseAsync(admin, TrialTemplate);
        return new SeededProviderTemplate(
            TrialTemplate,
            owner.MfaKey,
            owner.SessionSigningKey,
            owner.OwnerPassword,
            owner.OwnerSeed,
            clock.GetUtcNow(),
            managerSeed,
            managerCookies,
            policyId,
            policyRevision);
    }

    private static Dictionary<string, string?> BuildTemplateProviderSettings(string mfaKey, string signingKey) => new()
    {
        ["AdminMfa:ActiveKeyId"] = "fixture",
        ["AdminMfa:Keys:fixture"] = mfaKey,
        ["AdminSessions:SigningKey"] = signingKey,
        ["AdminFixtures:AdminId"] = "11111111-1111-1111-1111-111111111111",
        ["AdminFixtures:Capabilities"] = P0206Fixture.Manager,
        ["AdminFixtures:SessionDigest"] = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(P0206Fixture.FixtureToken))),
    };

    private static async Task FreezeTemplateDatabaseAsync(NpgsqlConnectionStringBuilder admin, string templateName)
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync(admin, $"""
            ALTER DATABASE {templateName} WITH ALLOW_CONNECTIONS false;
            SELECT pg_terminate_backend(pid, 5000) FROM pg_stat_activity
            WHERE datname = '{templateName}' AND pid <> pg_backend_pid();
            ALTER DATABASE {templateName} WITH IS_TEMPLATE true;
            """);
    }
}
