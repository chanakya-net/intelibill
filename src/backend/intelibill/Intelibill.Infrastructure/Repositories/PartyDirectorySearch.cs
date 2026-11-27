namespace Intelibill.Infrastructure.Repositories;

internal static class PartyDirectorySearch
{
    // An explicit PostgreSQL escape character keeps %, _ and backslash literal.
    internal static string Pattern(string search) => "%" + search
        .Replace(@"\", @"\\", StringComparison.Ordinal)
        .Replace("%", @"\%", StringComparison.Ordinal)
        .Replace("_", @"\_", StringComparison.Ordinal) + "%";
}
