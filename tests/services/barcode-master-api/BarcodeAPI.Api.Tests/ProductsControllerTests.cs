using BarcodeAPI.Api.Contracts;
using BarcodeAPI.Api.Controllers;
using BarcodeAPI.Domain.Entities;
using BarcodeAPI.Domain.Interfaces.Clients;
using BarcodeAPI.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarcodeAPI.Api.Tests;

public sealed class ProductsControllerTests
{
    [Fact]
    public async Task GetByBarcode_ReturnsLocalProductWithoutCallingOpenFoodFacts()
    {
        var product = new Product("123", "Test Product");
        var repository = new FakeProductRepository
        {
            OnGetByBarcode = barcode =>
            {
                Assert.Equal("123", barcode);
                return product;
            }
        };
        var openFoodFactsClient = new FakeOpenFoodFactsClient();

        var controller = new ProductsController(repository, openFoodFactsClient, NullLogger<ProductsController>.Instance);

        var result = await controller.GetByBarcode(" 123 ", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ProductDto>(ok.Value);

        Assert.Equal(product.Id, dto.Id);
        Assert.Equal("123", dto.Barcode);
        Assert.Equal("Test Product", dto.Name);
        Assert.Equal(product.CreatedAt, dto.CreatedAt);
        Assert.False(openFoodFactsClient.WasCalled);
    }

    [Fact]
    public async Task GetByBarcode_PersistsOpenFoodFactsHitAndReturnsDto()
    {
        var repository = new FakeProductRepository { OnGetByBarcode = _ => null };
        var openFoodFactsClient = new FakeOpenFoodFactsClient
        {
            OnGetByBarcode = barcode => new Product(barcode, "External Product")
        };

        var controller = new ProductsController(repository, openFoodFactsClient, NullLogger<ProductsController>.Instance);

        var result = await controller.GetByBarcode(" 456 ", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ProductDto>(ok.Value);

        Assert.Equal("456", dto.Barcode);
        Assert.Equal("External Product", dto.Name);
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal(repository.AddedProduct!.Id, dto.Id);
        Assert.Equal(repository.AddedProduct.CreatedAt, dto.CreatedAt);
        Assert.Equal("456", repository.AddedProduct.Barcode);
        Assert.Equal("External Product", repository.AddedProduct.Name);
        Assert.True(openFoodFactsClient.WasCalled);
    }

    [Fact]
    public async Task GetByBarcode_ExternalMiss_Returns404()
    {
        var repository = new FakeProductRepository { OnGetByBarcode = _ => null };
        var openFoodFactsClient = new FakeOpenFoodFactsClient { OnGetByBarcode = _ => null };
        var logger = new ListLogger<ProductsController>();

        var controller = new ProductsController(repository, openFoodFactsClient, logger);

        var result = await controller.GetByBarcode("789", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Null(repository.AddedProduct);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Information &&
                     entry.Message.Contains("Open Food Facts returned no usable product", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetByBarcode_ExternalFailure_Returns404()
    {
        var repository = new FakeProductRepository { OnGetByBarcode = _ => null };
        var openFoodFactsClient = new FakeOpenFoodFactsClient
        {
            ThrowOnGetByBarcode = new HttpRequestException("timeout")
        };
        var logger = new ListLogger<ProductsController>();

        var controller = new ProductsController(repository, openFoodFactsClient, logger);

        var result = await controller.GetByBarcode("789", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Null(repository.AddedProduct);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                     entry.Message.Contains("Open Food Facts lookup failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetByBarcode_SaveFailure_ReturnsSyntheticDto()
    {
        var repository = new FakeProductRepository
        {
            OnGetByBarcode = _ => null,
            ThrowOnAdd = new InvalidOperationException("DB unavailable")
        };
        var externalProduct = new Product("321", "Synth Product");
        var openFoodFactsClient = new FakeOpenFoodFactsClient
        {
            OnGetByBarcode = _ => externalProduct
        };

        var logger = new ListLogger<ProductsController>();
        var controller = new ProductsController(repository, openFoodFactsClient, logger);

        var result = await controller.GetByBarcode("321", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ProductDto>(ok.Value);

        Assert.Equal("321", dto.Barcode);
        Assert.Equal("Synth Product", dto.Name);
        Assert.Equal(externalProduct.Id, dto.Id);
        Assert.Equal(externalProduct.CreatedAt, dto.CreatedAt);

        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                     entry.Message.Contains("Failed to persist Open Food Facts product", StringComparison.Ordinal));
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                     entry.Message.Contains("Returning synthetic product", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetByBarcode_DuplicateRace_ReturnsPersistedProduct()
    {
        var persistedProduct = new Product("321", "Persisted Product");
        var callCount = 0;
        var repository = new FakeProductRepository
        {
            OnGetByBarcode = _ =>
            {
                callCount++;
                return callCount == 1 ? null : persistedProduct;
            },
            ThrowOnAdd = new InvalidOperationException("unique constraint violation")
        };
        var openFoodFactsClient = new FakeOpenFoodFactsClient
        {
            OnGetByBarcode = barcode => new Product(barcode, "Race Loser Product")
        };

        var logger = new ListLogger<ProductsController>();
        var controller = new ProductsController(repository, openFoodFactsClient, logger);

        var result = await controller.GetByBarcode("321", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ProductDto>(ok.Value);

        Assert.Equal(persistedProduct.Id, dto.Id);
        Assert.Equal("Persisted Product", dto.Name);

        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Information &&
                     entry.Message.Contains("Concurrent save resolved", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetByBarcode_SaveAndReadFailure_ReturnsSyntheticDto()
    {
        var callCount = 0;
        var repository = new FakeProductRepository
        {
            OnGetByBarcode = _ =>
            {
                callCount++;
                return callCount == 1
                    ? null
                    : throw new TimeoutException("lookup failed");
            },
            ThrowOnAdd = new InvalidOperationException("DB unavailable")
        };
        var externalProduct = new Product("654", "Fallback Product");
        var openFoodFactsClient = new FakeOpenFoodFactsClient
        {
            OnGetByBarcode = _ => externalProduct
        };

        var logger = new ListLogger<ProductsController>();
        var controller = new ProductsController(repository, openFoodFactsClient, logger);

        var result = await controller.GetByBarcode("654", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<ProductDto>(ok.Value);

        Assert.Equal(externalProduct.Id, dto.Id);
        Assert.Equal(externalProduct.CreatedAt, dto.CreatedAt);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                     entry.Message.Contains("Failed to re-read barcode", StringComparison.Ordinal));
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                     entry.Message.Contains("Returning synthetic product", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lookup_TrimsBarcodesAndReturnsProductsAndMissingBarcodes()
    {
        var product = new Product("123", "Test Product");
        var repository = new FakeProductRepository
        {
            OnGetByBarcodes = barcodes =>
            {
                Assert.Equal(["123", "456"], barcodes);
                return [product];
            }
        };
        var openFoodFactsClient = new FakeOpenFoodFactsClient();

        var controller = new ProductsController(repository, openFoodFactsClient, NullLogger<ProductsController>.Instance);

        var result = await controller.Lookup(
            new ProductLookupRequest([" 123 ", "", " 456 "]),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ProductLookupResponse>(ok.Value);
        var dto = Assert.Single(response.Products);

        Assert.Equal(product.Id, dto.Id);
        Assert.Equal("123", dto.Barcode);
        Assert.Equal("Test Product", dto.Name);
        Assert.Equal(["456"], response.NotFound);
    }

    private sealed class FakeProductRepository : IProductRepository
    {
        public Func<string, Product?>? OnGetByBarcode { get; set; }
        public Func<List<string>, List<Product>>? OnGetByBarcodes { get; set; }
        public Product? AddedProduct { get; private set; }
        public Exception? ThrowOnAdd { get; set; }

        public Task AddAsync(Product product, CancellationToken ct = default)
        {
            if (ThrowOnAdd is not null)
                throw ThrowOnAdd;
            AddedProduct = product;
            return Task.CompletedTask;
        }

        public Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct = default)
            => Task.FromResult(OnGetByBarcode?.Invoke(barcode));

        public Task<List<Product>> GetByBarcodesAsync(List<string> barcodes, CancellationToken ct = default)
            => Task.FromResult(OnGetByBarcodes?.Invoke(barcodes) ?? []);

        public Task<List<(string Barcode, string Name, string Reason)>> AddBatchAsync(
            List<Product> products,
            CancellationToken ct = default)
            => Task.FromResult<List<(string Barcode, string Name, string Reason)>>([]);
    }

    private sealed class FakeOpenFoodFactsClient : IOpenFoodFactsClient
    {
        public bool WasCalled { get; private set; }
        public Func<string, Product?>? OnGetByBarcode { get; set; }
        public Exception? ThrowOnGetByBarcode { get; set; }

        public Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct = default)
        {
            WasCalled = true;
            if (ThrowOnGetByBarcode is not null)
                throw ThrowOnGetByBarcode;
            return Task.FromResult(OnGetByBarcode?.Invoke(barcode));
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose() { }
        }
    }
}
