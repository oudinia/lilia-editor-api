using System.Collections.Concurrent;
using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Caches per user, kept in Redis (the user, 18 Oct: "add redis and cache in it per user"). A
/// stand-in store plays Redis here; the real one is checked on the test copy.
/// </summary>
[Collection("UserCache")] // the store is process-wide: these tests take turns
public class UserCacheTests
{
    private sealed class FakeStore : IUserCacheStore
    {
        public readonly ConcurrentDictionary<string, byte[]> Data = new();
        public bool Down;
        public Task<byte[]?> GetAsync(string scope, string kind, string key) =>
            Down ? throw new TimeoutException("redis down") : Task.FromResult(Data.TryGetValue($"{scope}:{kind}:{key}", out var v) ? v : null);
        public Task SetAsync(string scope, string kind, string key, byte[] value, TimeSpan ttl)
        {
            if (Down) throw new TimeoutException("redis down");
            Data[$"{scope}:{kind}:{key}"] = value;
            return Task.CompletedTask;
        }
        public Task<long> PurgeAsync(string scope)
        {
            var gone = Data.Keys.Where(k => k.StartsWith(scope + ":")).ToList();
            foreach (var k in gone) Data.TryRemove(k, out _);
            return Task.FromResult((long)gone.Count);
        }
    }

    private static CompileCache.Entry E(string text) => new(System.Text.Encoding.UTF8.GetBytes("%PDF " + text), "log", "aux");

    private static async Task<T> As<T>(string? user, Func<Task<T>> work) => await Task.Run(async () =>
    {
        CacheOwner.Set(user);
        return await work();
    });

    [Fact]
    public async Task One_user_s_compile_is_not_another_user_s_and_survives_a_restart_in_the_store()
    {
        var store = new FakeStore();
        var before = UserCache.Store;
        UserCache.Store = store;
        try
        {
            var key = Guid.NewGuid().ToString("N");
            var runs = 0;
            Task<CompileCache.Entry> Compile() { Interlocked.Increment(ref runs); return Task.FromResult(E("ada")); }

            var cache = new CompileCache();
            (await As("ada", () => cache.GetOrCompileAsync(key, Compile))).Hit.Should().BeFalse();
            (await As("ada", () => cache.GetOrCompileAsync(key, Compile))).Hit.Should().BeTrue();
            (await As("bob", () => cache.GetOrCompileAsync(key, Compile))).Hit.Should().BeFalse("per user: Bob's cache is his own");
            runs.Should().Be(2);

            // A restart: a new process's memory is empty, the store still has Ada's entry.
            var restarted = new CompileCache();
            var (entry, hit) = await As("ada", () => restarted.GetOrCompileAsync(key, Compile));
            hit.Should().BeTrue("served from the store after the restart");
            entry.Pdf.Should().Equal(E("ada").Pdf);
            runs.Should().Be(2);

            // Outside a request (no owner) nothing is read from or written to the store.
            var count = store.Data.Count;
            await As<(CompileCache.Entry, bool)>(null, () => restarted.GetOrCompileAsync(Guid.NewGuid().ToString("N"), Compile));
            store.Data.Count.Should().Be(count);
        }
        finally { UserCache.Store = before; }
    }

    [Fact]
    public async Task Closing_an_account_removes_everything_kept_for_that_user_only()
    {
        var store = new FakeStore();
        var before = UserCache.Store;
        UserCache.Store = store;
        try
        {
            var key = Guid.NewGuid().ToString("N");
            var ada = $"ada-{Guid.NewGuid():N}";
            var bob = $"bob-{Guid.NewGuid():N}";
            await As(ada, () => CompileCache.Shared.GetOrCompileAsync(key, () => Task.FromResult(E("a"))));
            await As(bob, () => CompileCache.Shared.GetOrCompileAsync(key, () => Task.FromResult(E("b"))));

            (await UserCache.PurgeUserAsync(ada)).Should().BeGreaterThanOrEqualTo(2, "memory and store");

            var runs = 0;
            (await As(ada, () => CompileCache.Shared.GetOrCompileAsync(key, () => { runs++; return Task.FromResult(E("a")); })))
                .Hit.Should().BeFalse("Ada's entries are gone");
            (await As(bob, () => CompileCache.Shared.GetOrCompileAsync(key, () => { runs++; return Task.FromResult(E("b")); })))
                .Hit.Should().BeTrue("Bob's are untouched");
            runs.Should().Be(1);
        }
        finally { UserCache.Store = before; }
    }

    [Fact]
    public async Task A_redis_that_is_down_never_fails_a_compile()
    {
        var before = UserCache.Store;
        UserCache.Store = new FakeStore { Down = true };
        try
        {
            var cache = new CompileCache();
            var key = Guid.NewGuid().ToString("N");
            (await As("ada", () => cache.GetOrCompileAsync(key, () => Task.FromResult(E("x"))))).Hit.Should().BeFalse();
            (await As("ada", () => cache.GetOrCompileAsync(key, () => Task.FromResult(E("x"))))).Hit.Should().BeTrue("memory still works");
        }
        finally { UserCache.Store = before; }
    }

    [Fact]
    public void Keys_never_carry_the_account_id()
    {
        CacheOwner.Scope("user-test-9bb2e08c").Should().NotContain("9bb2e08c").And.HaveLength(20);
        CacheOwner.Scope("ada").Should().Be(CacheOwner.Scope("ada")).And.NotBe(CacheOwner.Scope("bob"));
    }
}
