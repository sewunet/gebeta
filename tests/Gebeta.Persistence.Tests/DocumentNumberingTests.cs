using System.Collections.Concurrent;
using Gebeta.Application;
using Gebeta.Core.Infrastructure.Persistence;
using Gebeta.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Gebeta.Persistence.Tests;

/// <summary>Gap-free numbering against a real PostgreSQL: concurrency, rollback and the transaction rule.</summary>
public sealed class DocumentNumberingTests : IAsyncLifetime
{
    private static readonly Guid CompanyId = Guid.CreateVersion7();
    private static readonly DateOnly Today = new(2026, 11, 14);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = NewContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    private CoreDbContext NewContext() => new(new DbContextOptionsBuilder<CoreDbContext>()
        .UseNpgsql(_postgres.GetConnectionString(), o => o.MigrationsHistoryTable(CoreDbContext.MigrationsHistoryTable, CoreDbContext.SchemaName))
        .UseSnakeCaseNamingConvention()
        .Options);

    private static DocumentNumbering Numbering(string? configuredPattern = null) =>
        new(new FakeDirectory(), new FakeSettings(configuredPattern));

    [Fact]
    public async Task Concurrent_documents_get_unique_numbers_without_gaps()
    {
        var numbers = new ConcurrentBag<string>();
        var ct = TestContext.Current.CancellationToken;

        await Parallel.ForEachAsync(Enumerable.Range(0, 25), new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (_, token) =>
        {
            await using var db = NewContext();
            await using var tx = await db.Database.BeginTransactionAsync(token);
            numbers.Add(await Numbering().NextAsync(db, CoreDbContext.SchemaName, CompanyId, "Test Invoice", "TINV-.FY.-.#####", Today, token));
            await tx.CommitAsync(token);
        });

        Assert.Equal(Enumerable.Range(1, 25).Select(n => $"TINV-2019-{n:00000}").Order(), numbers.Order());
    }

    [Fact]
    public async Task A_rolled_back_save_gives_its_number_back()
    {
        var ct = TestContext.Current.CancellationToken;

        await using (var db = NewContext())
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            Assert.Equal("RB-00001", await Numbering().NextAsync(db, CoreDbContext.SchemaName, CompanyId, "Rollback", "RB-.#####", Today, ct));
            await tx.RollbackAsync(ct);
        }

        await using (var db = NewContext())
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            Assert.Equal("RB-00001", await Numbering().NextAsync(db, CoreDbContext.SchemaName, CompanyId, "Rollback", "RB-.#####", Today, ct));
            await tx.CommitAsync(ct);
        }
    }

    [Fact]
    public async Task The_company_pattern_overrides_the_module_default_and_each_year_restarts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = NewContext();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var numbering = Numbering("PCR-.ABBR.-.FY.-.####");

        var fy2019 = await numbering.NextAsync(db, CoreDbContext.SchemaName, CompanyId, "Petty Cash Request", "IGNORED-.#####", Today, ct);
        var fy2020 = await numbering.NextAsync(db, CoreDbContext.SchemaName, CompanyId, "Petty Cash Request", "IGNORED-.#####", new DateOnly(2027, 7, 9), ct);
        await tx.CommitAsync(ct);

        Assert.Equal("PCR-TST-2019-0001", fy2019);
        Assert.Equal("PCR-TST-2020-0001", fy2020);
    }

    [Fact]
    public async Task Numbers_outside_a_transaction_are_refused()
    {
        await using var db = NewContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Numbering().NextAsync(db, CoreDbContext.SchemaName, CompanyId, "X", "X-.#####", Today, TestContext.Current.CancellationToken));
    }

    private sealed class FakeDirectory : ICompanyDirectory
    {
        public Task<CompanyInfo?> GetAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<CompanyInfo?>(new CompanyInfo(companyId, "Test PLC", "TST", "ETB", true, "0012345678"));
    }

    private sealed class FakeSettings(string? pattern) : INamingSeriesSettings
    {
        public Task<string?> GetPatternAsync(Guid companyId, string documentType, CancellationToken cancellationToken) => Task.FromResult(pattern);
    }
}
