using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Intelibill.Licensing.Infrastructure.Migrations;

/// <summary>
/// The complete licensing schema: tables, functions, triggers, row security policies,
/// privileges and seed rows, applied from <c>20261127063245_Baseline.sql</c>.
/// </summary>
/// <remarks>
/// The script was generated from <c>pg_dump</c> of a database built by the former
/// migration chain and verified to restore to an identical schema. Policies that named
/// the migrating role are bound to <c>current_user</c> when the script runs. Add schema
/// changes as new migrations after this one; never edit the script.
/// </remarks>
public partial class Baseline : Migration
{
    private const string Script = "Intelibill.Licensing.Infrastructure.Migrations.Baseline.sql";

    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(ReadScript());

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("The baseline cannot be reverted. Drop and recreate the database instead.");

    private static string ReadScript()
    {
        using var stream = typeof(Baseline).Assembly.GetManifestResourceStream(Script)
            ?? throw new InvalidOperationException($"Embedded migration script {Script} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
