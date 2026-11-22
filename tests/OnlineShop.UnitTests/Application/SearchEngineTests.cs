using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OnlineShop.Application.Common;
using OnlineShop.Application.Search;
using OnlineShop.Domain.Search;
using OnlineShop.Infrastructure.Persistence;
using OnlineShop.Infrastructure.Search;
using OnlineShop.UnitTests.TestSupport;

namespace OnlineShop.UnitTests.Application;

public sealed class SearchOptionsTests
{
    private static List<ValidationResult> Validate(SearchOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void The_database_needs_no_engine_settings_and_an_engine_needs_an_address_and_a_clean_prefix()
    {
        Assert.Empty(Validate(new SearchOptions()));
        Assert.False(new SearchOptions().UsesEngine);

        var missing = Validate(new SearchOptions { Provider = SearchProvider.OpenSearch });
        Assert.Contains(missing, r => r.MemberNames.Contains("Engine.Url"));
        Assert.Contains(Validate(new SearchOptions { Provider = SearchProvider.Elasticsearch, Engine = { Url = "ftp://search" } }), r => r.MemberNames.Contains("Engine.Url"));
        Assert.Contains(Validate(new SearchOptions { Provider = SearchProvider.OpenSearch, Engine = { Url = "http://search:9200", IndexPrefix = "Shop One" } }),
            r => r.MemberNames.Contains("Engine.IndexPrefix"));
        Assert.Empty(Validate(new SearchOptions { Provider = SearchProvider.OpenSearch, Engine = { Url = "http://search:9200", IndexPrefix = "shop-1" } }));
    }
}

public sealed class SearchEngineHealthTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_engine_serves_only_when_built_current_and_not_recently_failed()
    {
        var health = new SearchEngineHealth();
        Assert.False(health.CanServe(Now));
        Assert.True(health.SetReady(true));
        Assert.False(health.SetReady(true));
        Assert.True(health.CanServe(Now));

        Assert.True(health.SetBehind(true));
        Assert.False(health.CanServe(Now));
        health.SetBehind(false);

        Assert.True(health.Fail(Now, TimeSpan.FromSeconds(30)));
        Assert.False(health.Fail(Now, TimeSpan.FromSeconds(30)));
        Assert.False(health.CanServe(Now.AddSeconds(29)));
        Assert.False(health.CanTry(Now.AddSeconds(29)));
        Assert.True(health.CanServe(Now.AddSeconds(30)));
        Assert.True(health.Recover());
        Assert.False(health.Recover());
    }
}

public sealed class EngineProductSearchTests
{
    private sealed class DatabaseSearch : IDatabaseProductSearch
    {
        public static readonly Guid Hit = Guid.NewGuid();

        public int Calls { get; private set; }

        public Task<IReadOnlyDictionary<Guid, double>> MatchAsync(SearchTerms terms, string locale, int limit, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyDictionary<Guid, double>>(new Dictionary<Guid, double> { [Hit] = 1 });
        }

        public Task<string?> SimilarWordAsync(string word, string locale, CancellationToken cancellationToken = default) => Task.FromResult<string?>("database");
    }

    private readonly FakeSearchEngine _engine = new();
    private readonly DatabaseSearch _database = new();
    private readonly SearchEngineHealth _health = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private static readonly SearchTerms Terms = SearchTerms.From("lamp", ["lamp"], []);

    private EngineProductSearch Search() => new(_engine, _database, _health, _clock,
        Options.Create(new SearchOptions { Provider = SearchProvider.OpenSearch, Engine = { Url = "http://search:9200", RetrySeconds = 30 } }),
        NullLogger<EngineProductSearch>.Instance);

    [Fact]
    public async Task The_database_answers_until_the_engine_is_built()
    {
        Assert.Equal(DatabaseSearch.Hit, Assert.Single(await Search().MatchAsync(Terms, "en", 10)).Key);
        Assert.Equal(0, _engine.Calls);

        _health.SetReady(true);
        var engineHit = Guid.NewGuid();
        _engine.Matches[engineHit] = 3;
        _engine.Similar = "engine";
        Assert.Equal(engineHit, Assert.Single(await Search().MatchAsync(Terms, "en", 10)).Key);
        Assert.Equal("engine", await Search().SimilarWordAsync("lmap", "en"));
        Assert.Equal(1, _database.Calls);

        _health.SetBehind(true);
        Assert.Equal(DatabaseSearch.Hit, Assert.Single(await Search().MatchAsync(Terms, "en", 10)).Key);
    }

    [Fact]
    public async Task A_failing_engine_is_left_alone_for_a_while_then_tried_again()
    {
        _health.SetReady(true);
        _engine.Down = true;

        Assert.Equal(DatabaseSearch.Hit, Assert.Single(await Search().MatchAsync(Terms, "en", 10)).Key);
        Assert.Equal("database", await Search().SimilarWordAsync("lmap", "en"));
        Assert.Equal(1, _engine.Calls);

        _engine.Down = false;
        _engine.Matches[Guid.NewGuid()] = 2;
        _clock.Advance(TimeSpan.FromSeconds(31));
        Assert.NotEqual(DatabaseSearch.Hit, Assert.Single(await Search().MatchAsync(Terms, "en", 10)).Key);
        Assert.True(_health.CanServe(_clock.GetUtcNow().UtcDateTime));
    }
}

public sealed class SearchEngineSyncTests : DemoCatalogTestBase
{
    private static readonly SearchOptions Engine = new() { Provider = SearchProvider.OpenSearch, Engine = { Url = "http://search:9200", MaxLagSeconds = 300 } };
    private readonly FakeSearchEngine _engine = new();
    private readonly SearchEngineHealth _health = new();

    private SearchEngineSyncService Sync(AppDbContext db) => new(db, _engine, _health, Database.Clock, Options.Create(Store), Options.Create(Engine));

    /// <summary>The database stage with an engine configured: the rebuilt products are queued for the engine.</summary>
    private async Task IndexDatabaseAsync()
    {
        await using var db = Database.CreateContext();
        var index = new SearchIndexService(db, Database.Clock, Options.Create(Store), Options.Create(Engine));
        while (await index.ProcessOutboxAsync() > 0)
        {
        }
    }

    private async Task SyncAllAsync()
    {
        await using var db = Database.CreateContext();
        while (await Sync(db).ProcessAsync() > 0)
        {
        }
    }

    [Fact]
    public async Task A_new_engine_gets_a_full_build_then_only_the_changed_products()
    {
        await SeedDemoAsync();
        await IndexDatabaseAsync();
        await using (var db = Database.CreateContext())
        {
            Assert.True(await db.SearchEngineOutbox.AnyAsync());
            Assert.False(await Sync(db).EnsureIndexAsync());
            Assert.False(await Sync(db).EnsureIndexAsync());
            Assert.Equal(1, await db.SearchEngineOutbox.CountAsync(e => e.ProductId == Guid.Empty));
        }
        await SyncAllAsync();

        await using (var db = Database.CreateContext())
        {
            Assert.Equal(0, await db.SearchEngineOutbox.CountAsync());
            Assert.True(await Sync(db).EnsureIndexAsync());
            var published = await db.Products.CountAsync(p => p.IsPublished);
            foreach (var locale in Store.LocaleList)
                Assert.Equal(published, _engine.Live(locale).Count);
            // One index per language: the build's indexes are the live ones.
            Assert.Equal(Store.LocaleList.Count, _engine.Indexes.Count);

            var lamp = await db.Products.SingleAsync(p => p.SKU == "DEMO-FSH-001");
            lamp.Name = "کفش کوهنوردی";
            var bag = await db.Products.SingleAsync(p => p.SKU == "DEMO-FSH-002");
            bag.IsPublished = false;
            await db.SaveChangesAsync();
        }
        await IndexDatabaseAsync();
        await using (var db = Database.CreateContext())
            Assert.Equal(2, await db.SearchEngineOutbox.CountAsync());
        await SyncAllAsync();

        await using var check = Database.CreateContext();
        var shoes = await check.Products.SingleAsync(p => p.SKU == "DEMO-FSH-001");
        var gone = await check.Products.SingleAsync(p => p.SKU == "DEMO-FSH-002");
        Assert.Equal("کفش کوهنوردی", _engine.Live("fa")[shoes.Id].Title);
        Assert.All(Store.LocaleList, locale => Assert.False(_engine.Live(locale).ContainsKey(gone.Id)));
        Assert.Equal(0, await check.SearchEngineOutbox.CountAsync());
    }

    [Fact]
    public async Task A_rebuild_request_reaches_the_engine_as_one_full_build()
    {
        await SeedDemoAsync();
        await IndexDatabaseAsync();
        await using (var first = Database.CreateContext())
            await Sync(first).EnsureIndexAsync();
        await SyncAllAsync();
        await using var db = Database.CreateContext();
        await new SearchIndexService(db, Database.Clock, Options.Create(Store), Options.Create(Engine)).EnqueueAllAsync();
        await IndexDatabaseAsync();

        Assert.Equal(Guid.Empty, (await db.SearchEngineOutbox.SingleAsync()).ProductId);
        await SyncAllAsync();
        Assert.Equal(0, await db.SearchEngineOutbox.CountAsync());
        Assert.Equal(Store.LocaleList.Count, _engine.Indexes.Count);
    }

    [Fact]
    public async Task A_failing_engine_keeps_its_queue_and_a_long_wait_counts_as_behind()
    {
        await SeedDemoAsync();
        await IndexDatabaseAsync();
        await using var db = Database.CreateContext();
        var queued = await db.SearchEngineOutbox.CountAsync();
        _engine.Down = true;

        await Assert.ThrowsAsync<SearchEngineException>(() => Sync(db).ProcessAsync());
        Assert.Equal(queued, await db.SearchEngineOutbox.CountAsync());
        Assert.False(await Sync(db).IsBehindAsync());
        Database.Clock.Advance(TimeSpan.FromSeconds(301));
        Assert.True(await Sync(db).IsBehindAsync());

        Assert.Equal(new SearchEngineStatus(SearchProvider.OpenSearch, false, queued), await Sync(db).StatusAsync());
        var noEngine = new SearchEngineSyncService(db, _engine, _health, Database.Clock, Options.Create(Store), Options.Create(new SearchOptions()));
        Assert.Null(await noEngine.StatusAsync());
    }

    [Fact]
    public async Task The_worker_builds_the_engine_marks_it_ready_and_survives_an_outage()
    {
        await SeedDemoAsync();
        var services = new ServiceCollection();
        services.AddScoped(_ => Database.CreateContext());
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<TimeProvider>(Database.Clock);
        services.AddSingleton(Options.Create(Store));
        services.AddSingleton(Options.Create(Engine));
        services.AddSingleton<ISearchEngine>(_engine);
        services.AddSingleton(_health);
        services.AddScoped<SearchIndexService>();
        services.AddScoped<SearchEngineSyncService>();
        await using var provider = services.BuildServiceProvider();
        var worker = new SearchIndexWorker(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(Engine), Database.Clock, NullLogger<SearchIndexWorker>.Instance);

        await worker.RunAsync(ensureBuilt: true, CancellationToken.None);
        Assert.True(_health.CanServe(Database.Clock.GetUtcNow().UtcDateTime));
        Assert.NotEmpty(_engine.Live("en"));

        _engine.Down = true;
        await using (var db = Database.CreateContext())
        {
            var product = await db.Products.FirstAsync(p => p.IsPublished);
            product.Name += " 2";
            await db.SaveChangesAsync();
        }
        await worker.RunAsync(ensureBuilt: false, CancellationToken.None);
        var now = Database.Clock.GetUtcNow().UtcDateTime;
        Assert.False(_health.CanTry(now));
        var calls = _engine.Calls;
        // Within the retry period the worker leaves the engine alone.
        await worker.SyncEngineAsync(CancellationToken.None);
        Assert.Equal(calls, _engine.Calls);

        _engine.Down = false;
        Database.Clock.Advance(TimeSpan.FromSeconds(31));
        await worker.SyncEngineAsync(CancellationToken.None);
        Assert.True(_health.CanServe(Database.Clock.GetUtcNow().UtcDateTime));
        await using var check = Database.CreateContext();
        Assert.Equal(0, await check.SearchEngineOutbox.CountAsync());
    }
}
