using BarcodeAPI.Domain.Entities;
using BarcodeAPI.Domain.Interfaces.Repositories;
using BarcodeAPI.Infrastructure.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace BarcodeAPI.Infrastructure.Repositories;

public class ProductRepository(ApplicationDbContext context) : IProductRepository
{
    public async Task AddAsync(Product product, CancellationToken ct = default)
    {
        await context.Products.AddAsync(product, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        var connection = context.Database.GetDbConnection();
        const string sql = """SELECT "Id", "Barcode", "Name", "CreatedAt" FROM "Products" WHERE "Barcode" = @barcode LIMIT 1""";

        return await connection.QueryFirstOrDefaultAsync<Product>(
            new CommandDefinition(sql, new { barcode }, cancellationToken: ct));
    }

    public async Task<List<Product>> GetByBarcodesAsync(
        List<string> barcodes, CancellationToken ct = default)
    {
        if (barcodes.Count == 0)
            return [];

        var connection = context.Database.GetDbConnection();
        const string sql = """
            SELECT "Id", "Barcode", "Name", "CreatedAt"
            FROM "Products"
            WHERE "Barcode" = ANY(@barcodes)
            """;

        var result = await connection.QueryAsync<Product>(
            new CommandDefinition(sql, new { barcodes }, cancellationToken: ct));

        return result.ToList();
    }

    public async Task<List<(string Barcode, string Name, string Reason)>> AddBatchAsync(
        List<Product> products, CancellationToken ct = default)
    {
        var failures = new List<(string Barcode, string Name, string Reason)>();

        if (products.Count == 0)
            return failures;

        var connection = context.Database.GetDbConnection();

        // Step 1: detect duplicates already in DB
        var barcodes = products.Select(p => p.Barcode).ToList();
        const string existingSql = """SELECT "Barcode" FROM "Products" WHERE "Barcode" = ANY(@barcodes)""";
        var existingBarcodeSet = (await connection.QueryAsync<string>(
            new CommandDefinition(existingSql, new { barcodes }, cancellationToken: ct)))
            .ToHashSet();

        var toInsert = new List<Product>(products.Count);
        foreach (var p in products)
        {
            if (existingBarcodeSet.Contains(p.Barcode))
            {
                failures.Add((p.Barcode, p.Name, "DUPLICATE: Barcode already exists"));
            }
            else
            {
                toInsert.Add(p);
            }
        }

        if (toInsert.Count == 0)
            return failures;

        // Step 2: batch insert remaining products
        try
        {
            var ids = toInsert.Select(p => p.Id).ToArray();
            var barcodeArray = toInsert.Select(p => p.Barcode).ToArray();
            var nameArray = toInsert.Select(p => p.Name).ToArray();
            var createdAts = toInsert.Select(p => p.CreatedAt).ToArray();

            const string insertSql = """
                INSERT INTO "Products" ("Id", "Barcode", "Name", "CreatedAt")
                SELECT * FROM UNNEST(@ids, @barcodes, @names, @createdAts)
                """;

            await connection.ExecuteAsync(
                new CommandDefinition(
                    insertSql,
                    new { ids, barcodes = barcodeArray, names = nameArray, createdAts },
                    cancellationToken: ct));
        }
        catch (Exception ex)
        {
            // If entire batch insert blows up, mark all as failed (continues processing)
            foreach (var p in toInsert)
            {
                failures.Add((p.Barcode, p.Name, $"INSERT_ERROR: {ex.Message}"));
            }
        }

        return failures;
    }
}
