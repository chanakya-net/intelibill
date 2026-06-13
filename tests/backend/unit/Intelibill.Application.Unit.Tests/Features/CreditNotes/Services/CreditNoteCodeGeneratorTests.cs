using Intelibill.Application.Features.CreditNotes.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Intelibill.Application.Unit.Tests.Features.CreditNotes.Services;

public class CreditNoteCodeGeneratorTests
{
    private static CreditNote CreateStubCreditNote() => CreditNote.Issue(Guid.NewGuid(), Guid.NewGuid(), 100m, "test", "CN-20260101-123456", null).Value;

    [Fact]
    public async Task GenerateAsync_ReturnsCNPrefixUtcDateAndSixCharacterSuffix()
    {
        var repo = Substitute.For<ICreditNoteRepository>();
        repo.GetByCodeAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ReturnsNull();
        var generator = new CreditNoteCodeGenerator(repo);

        var shopId = Guid.NewGuid();
        var code = await generator.GenerateAsync(shopId);

        Assert.Matches("^CN-\\d{8}-[0-9A-F]{6}$", code);
    }

    [Fact]
    public async Task GenerateAsync_UsesUtcDateInFormat()
    {
        var repo = Substitute.For<ICreditNoteRepository>();
        repo.GetByCodeAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ReturnsNull();
        var generator = new CreditNoteCodeGenerator(repo);

        var shopId = Guid.NewGuid();
        var code = await generator.GenerateAsync(shopId);

        var datePart = code.Substring(3, 8);
        Assert.Matches("^\\d{8}$", datePart);
    }

    [Fact]
    public async Task GenerateAsync_SuffixIsUppercase()
    {
        var repo = Substitute.For<ICreditNoteRepository>();
        repo.GetByCodeAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ReturnsNull();
        var generator = new CreditNoteCodeGenerator(repo);

        var shopId = Guid.NewGuid();
        var code = await generator.GenerateAsync(shopId);

        var suffix = code.Substring(12, 6);
        Assert.Equal(suffix, suffix.ToUpperInvariant());
    }

    [Fact]
    public async Task GenerateAsync_RetriesOnCollision()
    {
        var repo = Substitute.For<ICreditNoteRepository>();
        var shopId = Guid.NewGuid();
        var callCount = 0;

        repo.GetByCodeAsync(shopId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return Task.FromResult((CreditNote?)CreateStubCreditNote());
                }
                return Task.FromResult((CreditNote?)null);
            });

        var generator = new CreditNoteCodeGenerator(repo);
        var code = await generator.GenerateAsync(shopId);

        Assert.NotNull(code);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GenerateAsync_ThrowsAfterMaxRetries()
    {
        var repo = Substitute.For<ICreditNoteRepository>();
        var shopId = Guid.NewGuid();

        repo.GetByCodeAsync(shopId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((CreditNote?)CreateStubCreditNote()));

        var generator = new CreditNoteCodeGenerator(repo);

        await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync(shopId));
    }
}
