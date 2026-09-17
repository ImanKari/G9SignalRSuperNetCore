using G9SignalRSuperNetCore.Server;
using G9SignalRSuperNetCore.Server.Classes.Filters;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace G9SignalRSuperNetCore.Tests;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void Core_services_are_registered_in_every_service_collection_and_once_per_collection()
    {
        // Up to 2.5.3 a process-wide flag skipped the registrations for every collection after the first: a second host
        // in the same process (a test server, a second app) ran without the hub filter or the user-id provider.
        var first = new ServiceCollection().AddLogging();
        var second = new ServiceCollection().AddLogging();

        first.AddSignalRSuperNetCoreCore();
        first.AddSignalRSuperNetCoreCore();
        second.AddSignalRSuperNetCoreCore();

        foreach (var services in new[] { first, second })
        {
            Assert.Single(services, d => d.ServiceType == typeof(G9CHubFilter));
            Assert.Single(services, d => d.ServiceType == typeof(IUserIdProvider));
            using var provider = services.BuildServiceProvider();
            Assert.NotNull(provider.GetRequiredService<G9CHubFilter>());
        }
    }

    [Fact]
    public async Task Jwt_authentication_is_registered_in_every_service_collection_and_once_per_collection()
    {
        var parameters = new TokenValidationParameters();
        var first = new ServiceCollection().AddLogging();
        var second = new ServiceCollection().AddLogging();

        first.AddSignalRSuperNetCoreJwt("/one", parameters);
        first.AddSignalRSuperNetCoreJwt("/two", parameters);
        second.AddSignalRSuperNetCoreJwt("/one", parameters);

        foreach (var services in new[] { first, second })
        {
            using var provider = services.BuildServiceProvider();
            var schemes = provider.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>();
            Assert.Single(await schemes.GetAllSchemesAsync(), s => s.Name == "Bearer");
        }
    }
}
