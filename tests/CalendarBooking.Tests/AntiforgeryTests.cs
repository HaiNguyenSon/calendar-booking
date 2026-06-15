using CalendarBooking.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System.Net;

namespace CalendarBooking.Tests;

/// <summary>
/// Boots the real app in-memory and checks that state-changing form POSTs are rejected
/// without an antiforgery token. Swaps Postgres for the in-memory provider and drops the
/// background workers so the test needs no Docker/database.
/// </summary>
public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            // Background workers need a real DB; not relevant to this test.
            services.RemoveAll<IHostedService>();

            // Swap the Npgsql DbContext for in-memory.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase("antiforgery-tests"));
        });
    }
}

public class AntiforgeryTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task Logout_POST_without_an_antiforgery_token_is_rejected()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/Account/Logout",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["returnUrl"] = "" }));

        // The antiforgery middleware rejects the tokenless form post before it does anything.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PerformExternalLogin_POST_without_an_antiforgery_token_is_rejected()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/Account/PerformExternalLogin",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["provider"] = "Google", ["returnUrl"] = "" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
