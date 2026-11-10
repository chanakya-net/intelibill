using BarcodeAPI.Api.Contracts;
using BarcodeAPI.Domain.Entities;
using BarcodeAPI.Domain.Interfaces.Clients;
using BarcodeAPI.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BarcodeAPI.Api.Controllers;

[ApiController]
[Route("api/products")]
[Authorize(Policy = "DualAuth")]
public sealed class ProductsController(
    IProductRepository repository,
    IOpenFoodFactsClient openFoodFactsClient,
    ILogger<ProductsController> logger)
    : ControllerBase
{
    [HttpGet("{barcode}", Name = "GetProductByBarcode")]
    public async Task<ActionResult<ProductDto>> GetByBarcode(string barcode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return BadRequest(new { error = "Barcode is required." });
        }

        var normalizedBarcode = barcode.Trim();
        var product = await repository.GetByBarcodeAsync(normalizedBarcode, ct);

        if (product is not null)
        {
            return Ok(ToDto(product));
        }

        logger.LogInformation("Local miss for barcode {Barcode}; attempting Open Food Facts fallback.", normalizedBarcode);

        Product? externalProduct;
        try
        {
            externalProduct = await openFoodFactsClient.GetByBarcodeAsync(normalizedBarcode, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Open Food Facts lookup failed for barcode {Barcode}.", normalizedBarcode);
            return NotFound(new { barcode, error = "Product not found." });
        }

        if (externalProduct is null)
        {
            logger.LogInformation("Open Food Facts returned no usable product for barcode {Barcode}.", normalizedBarcode);
            return NotFound(new { barcode, error = "Product not found." });
        }

        try
        {
            await repository.AddAsync(externalProduct, ct);
            return Ok(ToDto(externalProduct));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist Open Food Facts product for barcode {Barcode}; checking for concurrent save.", normalizedBarcode);

            try
            {
                var persisted = await repository.GetByBarcodeAsync(normalizedBarcode, ct);
                if (persisted is not null)
                {
                    logger.LogInformation("Concurrent save resolved for barcode {Barcode}; returning persisted product.", normalizedBarcode);
                    return Ok(ToDto(persisted));
                }
            }
            catch (Exception lookupEx)
            {
                logger.LogWarning(lookupEx, "Failed to re-read barcode {Barcode} after save failure.", normalizedBarcode);
            }

            logger.LogWarning("Returning synthetic product for barcode {Barcode} after save failure.", normalizedBarcode);
            return Ok(ToDto(externalProduct));
        }
    }

    [HttpPost("lookup", Name = "LookupProducts")]
    public async Task<ActionResult<ProductLookupResponse>> Lookup(
        ProductLookupRequest request,
        CancellationToken ct)
    {
        if (request.Barcodes is null || request.Barcodes.Count == 0)
        {
            return BadRequest(new { error = "Barcodes list is required and cannot be empty." });
        }

        var barcodes = request.Barcodes
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(b => b.Trim())
            .ToList();

        if (barcodes.Count == 0)
        {
            return BadRequest(new { error = "No valid barcodes provided." });
        }

        var products = await repository.GetByBarcodesAsync(barcodes, ct);
        var foundBarcodes = products.Select(p => p.Barcode).ToHashSet();
        var notFound = barcodes.Where(b => !foundBarcodes.Contains(b)).ToList();

        var response = new ProductLookupResponse(
            products.Select(ToDto).ToList(),
            notFound);

        return Ok(response);
    }

    [HttpPost("batch", Name = "BatchCreateProducts")]
    public async Task<ActionResult<BatchProductResponse>> Batch(
        BatchProductRequest request,
        CancellationToken ct)
    {
        if (request.Products is null || request.Products.Count == 0)
        {
            return BadRequest(new { error = "Products list is required and cannot be empty." });
        }

        var products = new List<Product>(request.Products.Count);
        var validationFailures = new List<FailedProductItem>();

        foreach (var item in request.Products)
        {
            if (string.IsNullOrWhiteSpace(item.Barcode))
            {
                validationFailures.Add(new FailedProductItem(
                    item.Barcode ?? "", item.Name ?? "", "INVALID: Barcode is required."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Name))
            {
                validationFailures.Add(new FailedProductItem(
                    item.Barcode, item.Name ?? "", "INVALID: Name is required."));
                continue;
            }

            products.Add(new Product(item.Barcode.Trim(), item.Name.Trim()));
        }

        var dbFailures = await repository.AddBatchAsync(products, ct);

        var allFailures = validationFailures
            .Concat(dbFailures.Select(f => new FailedProductItem(f.Barcode, f.Name, f.Reason)))
            .ToList();

        var response = new BatchProductResponse(
            TotalReceived: request.Products.Count,
            SuccessfulCount: request.Products.Count - allFailures.Count,
            FailedCount: allFailures.Count,
            FailedRecords: allFailures);

        return Ok(response);
    }

    private static ProductDto ToDto(Product product)
        => new(product.Id, product.Barcode, product.Name, product.CreatedAt);
}
