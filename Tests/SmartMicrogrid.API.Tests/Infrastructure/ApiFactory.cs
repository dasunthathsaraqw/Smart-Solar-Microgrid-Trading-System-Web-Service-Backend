/**
 * File: ApiFactory.cs
 * Purpose: Shared in-memory API host backed by one disposable local MongoDB database per test run.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace SmartMicrogrid.API.Tests.Infrastructure;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string MongoConnection = "mongodb://127.0.0.1:27017/?serverSelectionTimeoutMS=2000&connectTimeoutMS=1000";
    private readonly MongoClient _mongoClient = new(MongoConnection);
    private readonly Dictionary<string, string?> _originalEnvironment = new();

    public string DatabaseName { get; } = $"SmartSolarTests_{Guid.NewGuid():N}";
    public IMongoDatabase Database => _mongoClient.GetDatabase(DatabaseName);

    // Overrides real configuration with an isolated database, test-only JWT key and disabled sample seeding.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDB:ConnectionString"] = MongoConnection,
                ["MongoDB:DatabaseName"] = DatabaseName,
                ["Jwt:Key"] = new string('T', 64),
                ["Seeding:SeedSampleData"] = "false",
                ["Hosting:UseHttpsRedirection"] = "false",
            });
        });
    }

    // Fails promptly with a useful message when the local MongoDB prerequisite is unavailable.
    public async Task InitializeAsync()
    {
        // Minimal-hosting top-level code reads builder.Configuration before WebApplicationFactory's
        // deferred ConfigureAppConfiguration callback, so process-local overrides are also required.
        SetOverride("MongoDB__ConnectionString", MongoConnection);
        SetOverride("MongoDB__DatabaseName", DatabaseName);
        SetOverride("Jwt__Key", new string('T', 64));
        SetOverride("Seeding__SeedSampleData", "false");
        SetOverride("Hosting__UseHttpsRedirection", "false");

        try
        {
            await Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Local MongoDB is not reachable at 127.0.0.1:27017. Start MongoDB before running dotnet test.", ex);
        }

        using var client = CreateClient();
        var health = await client.GetAsync("/api/health");
        Assert.True(health.IsSuccessStatusCode, $"Test API failed to start: {await health.Content.ReadAsStringAsync()}");
    }

    // Drops this run's database and checks that cleanup completed before disposing the host.
    async Task IAsyncLifetime.DisposeAsync()
    {
        try
        {
            await _mongoClient.DropDatabaseAsync(DatabaseName);
            using var cursor = await _mongoClient.ListDatabaseNamesAsync();
            var names = await cursor.ToListAsync();
            Assert.DoesNotContain(DatabaseName, names);
        }
        finally
        {
            Dispose();
            foreach (var (name, value) in _originalEnvironment)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    // Saves and sets a process-local configuration override for early minimal-hosting startup code.
    private void SetOverride(string name, string value)
    {
        _originalEnvironment[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }
}

// Shares one API host and disposable database across all integration test classes.
[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory> { }
