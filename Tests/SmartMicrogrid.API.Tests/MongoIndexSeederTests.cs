/**
 * File: MongoIndexSeederTests.cs
 * Purpose: Integration checks for the EnergyReservation indexes used by operator workflows.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using SmartMicrogrid.API.Data;
using SmartMicrogrid.API.Models;
using SmartMicrogrid.API.Services;
using SmartMicrogrid.API.Tests.Infrastructure;
using Xunit;

namespace SmartMicrogrid.API.Tests;

[Collection("Api")]
public sealed class MongoIndexSeederTests
{
    private readonly ApiFactory _factory;

    // Initializes index checks against the shared disposable API database.
    public MongoIndexSeederTests(ApiFactory factory)
    {
        _factory = factory;
    }

    // Rule: repeated seeding preserves the three exact EnergyReservation index definitions.
    [Fact]
    public async Task CreateAsync_Repeatedly_CreatesExpectedReservationIndexes()
    {
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<IMongoDbService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<MongoIndexSeederTests>>();

        await MongoIndexSeeder.CreateAsync(database, logger);
        await MongoIndexSeeder.CreateAsync(database, logger);

        var indexes = await database.Reservations.Indexes.List().ToListAsync();
        var qrToken = FindIndex(indexes, "ux_reservations_qrToken_string");
        var completedHistory = FindIndex(indexes, "ix_reservations_status_completedAt");
        var stationHistory = FindIndex(indexes, "ix_reservations_stationId_status_completedAt");

        Assert.Equal(new BsonDocument("qrToken", 1), qrToken["key"].AsBsonDocument);
        Assert.True(qrToken["unique"].AsBoolean);
        var qrType = qrToken["partialFilterExpression"]["qrToken"]["$type"];
        Assert.True(
            qrType == new BsonInt32((int)BsonType.String) || qrType == new BsonString("string"),
            $"Expected a BSON string partial filter, but found {qrType}.");
        Assert.Equal(
            new BsonDocument { { "status", 1 }, { "completedAt", -1 } },
            completedHistory["key"].AsBsonDocument);
        Assert.Equal(
            new BsonDocument { { "stationId", 1 }, { "status", 1 }, { "completedAt", -1 } },
            stationHistory["key"].AsBsonDocument);
    }

    // Rule: the partial unique QR index excludes nulls but rejects duplicate string tokens.
    [Fact]
    public async Task QrTokenIndex_AllowsNullsAndRejectsDuplicateStrings()
    {
        var reservations = _factory.Database.GetCollection<EnergyReservation>("EnergyReservation");
        var marker = $"index-test-{Guid.NewGuid():N}";
        var duplicateToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        try
        {
            await reservations.InsertManyAsync(
            [
                new EnergyReservation { CreatedBy = marker, QrToken = null },
                new EnergyReservation { CreatedBy = marker, QrToken = null },
            ]);
            await reservations.InsertOneAsync(new EnergyReservation
            {
                CreatedBy = marker,
                QrToken = duplicateToken,
            });

            var duplicate = await Assert.ThrowsAsync<MongoWriteException>(() =>
                reservations.InsertOneAsync(new EnergyReservation
                {
                    CreatedBy = marker,
                    QrToken = duplicateToken,
                }));
            Assert.Equal(ServerErrorCategory.DuplicateKey, duplicate.WriteError.Category);
        }
        finally
        {
            await reservations.DeleteManyAsync(reservation => reservation.CreatedBy == marker);
        }
    }

    // Finds one deterministic index definition by name.
    private static BsonDocument FindIndex(IEnumerable<BsonDocument> indexes, string name)
    {
        return Assert.Single(indexes, index => index["name"].AsString == name);
    }
}
