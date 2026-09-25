using System.Net;
using System.Security.Claims;
using G9SignalRSuperNetCore.Server.Classes.Attributes;
using G9SignalRSuperNetCore.Server.Classes.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace G9SignalRSuperNetCore.Tests;

/// <summary>
///     Connection-limit counters (<see cref="G9AttrConnectionLimitAttribute" />) must be given back exactly once per
///     connection. Before 2.9.0 the disconnect path re-read the remote IP from the HTTP context, which is already
///     disposed after an abrupt close; the exception skipped both decrements (and the hub's own OnDisconnectedAsync), so
///     every such drop leaked a slot until the address was locked out with G9_CONNECTION_LIMIT.
/// </summary>
public sealed class ConnectionLimitLifetimeTests
{
    [Fact]
    public async Task A_disposed_http_context_at_disconnect_still_releases_the_slots_and_runs_the_hub()
    {
        var filter = new G9CHubFilter();
        var services = new ServiceCollection().BuildServiceProvider();

        var first = new FakeCaller("c1", "alice", IPAddress.Parse("10.0.0.7"));
        await filter.OnConnectedAsync(new HubLifetimeContext(first, services, new LimitedHub()), _ => Task.CompletedTask);

        // Limits are 1 per user and 1 per IP: a second connection is refused while the first is open.
        var second = new FakeCaller("c2", "alice", IPAddress.Parse("10.0.0.7"));
        var refused = () => filter.OnConnectedAsync(new HubLifetimeContext(second, services, new LimitedHub()), _ => Task.CompletedTask);
        await Assert.ThrowsAsync<HubException>(refused);

        // The transport dies abruptly: the HTTP context features are gone when OnDisconnectedAsync runs.
        first.DisposeHttpContext();
        var hubCleanupRan = false;
        await filter.OnDisconnectedAsync(new HubLifetimeContext(first, services, new LimitedHub()), null, (_, _) =>
        {
            hubCleanupRan = true;
            return Task.CompletedTask;
        });
        Assert.True(hubCleanupRan, "the hub's own OnDisconnectedAsync must always run");

        var third = new FakeCaller("c3", "alice", IPAddress.Parse("10.0.0.7"));
        await filter.OnConnectedAsync(new HubLifetimeContext(third, services, new LimitedHub()), _ => Task.CompletedTask);
    }

    [Fact]
    public async Task A_connection_the_hub_refuses_gives_its_slots_back()
    {
        var filter = new G9CHubFilter();
        var services = new ServiceCollection().BuildServiceProvider();

        for (var i = 0; i < 3; i++)
        {
            var caller = new FakeCaller("refused-" + i, "bob", IPAddress.Parse("10.0.0.8"));
            var refusedByHub = () => filter.OnConnectedAsync(new HubLifetimeContext(caller, services, new LimitedHub()),
                _ => throw new HubException("app_refused"));
            var error = await Assert.ThrowsAsync<HubException>(refusedByHub);
            Assert.Equal("app_refused", error.Message); // never G9_CONNECTION_LIMIT: nothing leaked from the attempt before
        }

        var accepted = new FakeCaller("accepted", "bob", IPAddress.Parse("10.0.0.8"));
        await filter.OnConnectedAsync(new HubLifetimeContext(accepted, services, new LimitedHub()), _ => Task.CompletedTask);
    }

    [Fact]
    public async Task Each_hub_counts_its_own_connections()
    {
        var filter = new G9CHubFilter();
        var services = new ServiceCollection().BuildServiceProvider();

        // The user holds the one slot of LimitedHub; that must not use up OtherLimitedHub's slot (and vice versa).
        var main = new FakeCaller("main", "carol", IPAddress.Parse("10.0.0.9"));
        await filter.OnConnectedAsync(new HubLifetimeContext(main, services, new LimitedHub()), _ => Task.CompletedTask);
        var meet = new FakeCaller("meet", "carol", IPAddress.Parse("10.0.0.9"));
        await filter.OnConnectedAsync(new HubLifetimeContext(meet, services, new OtherLimitedHub()), _ => Task.CompletedTask);

        var secondMeet = new FakeCaller("meet-2", "carol", IPAddress.Parse("10.0.0.9"));
        var refused = () => filter.OnConnectedAsync(new HubLifetimeContext(secondMeet, services, new OtherLimitedHub()), _ => Task.CompletedTask);
        await Assert.ThrowsAsync<HubException>(refused);
    }

    [G9AttrConnectionLimit(perUser: 1, perIp: 1)]
    private sealed class LimitedHub : Hub;

    [G9AttrConnectionLimit(perUser: 1, perIp: 1)]
    private sealed class OtherLimitedHub : Hub;

    private sealed class FakeCaller : HubCallerContext
    {
        private readonly DisposableFeatures _features;

        public FakeCaller(string connectionId, string user, IPAddress ip)
        {
            ConnectionId = connectionId;
            UserIdentifier = user;
            var http = new DefaultHttpContext { Connection = { RemoteIpAddress = ip } };
            _features = new DisposableFeatures(http);
        }

        public override string ConnectionId { get; }
        public override string? UserIdentifier { get; }
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features => _features;
        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public void DisposeHttpContext() => _features.Disposed = true;

        public override void Abort()
        {
        }
    }

    /// <summary>Behaves like Kestrel's collection after the request ended: every access throws.</summary>
    private sealed class DisposableFeatures(HttpContext http) : IFeatureCollection
    {
        private readonly FeatureCollection _inner = CreateInner(http);

        public bool Disposed { get; set; }

        public bool IsReadOnly => false;
        public int Revision => Check()._inner.Revision;

        public object? this[Type key]
        {
            get => Check()._inner[key];
            set => Check()._inner[key] = value;
        }

        public TFeature? Get<TFeature>() => Check()._inner.Get<TFeature>();
        public void Set<TFeature>(TFeature? instance) => Check()._inner.Set(instance);
        public IEnumerator<KeyValuePair<Type, object>> GetEnumerator() => Check()._inner.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private DisposableFeatures Check() => Disposed ? throw new ObjectDisposedException(nameof(IFeatureCollection)) : this;

        private static FeatureCollection CreateInner(HttpContext http)
        {
            var features = new FeatureCollection();
            features.Set<IHttpContextFeature>(new HttpContextFeature { HttpContext = http });
            return features;
        }
    }

    private sealed class HttpContextFeature : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; }
    }
}
