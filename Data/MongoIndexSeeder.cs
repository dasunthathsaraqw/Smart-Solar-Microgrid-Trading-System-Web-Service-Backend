/**
 * File: MongoIndexSeeder.cs
 * Purpose: Adds unique database indexes to backstop application-level duplicate checks.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using MongoDB.Driver;
using SmartMicrogrid.API.Services;
using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Data;

public static class MongoIndexSeeder
{
    // Creates each unique index independently so duplicates in one collection do not block others.
    public static async Task CreateAsync(IMongoDbService db, ILogger logger)
    {
        try
        {
            await db.Users.Indexes.CreateOneAsync(new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(user => user.Email),
                new CreateIndexOptions { Name = "ux_users_email", Unique = true }));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not create unique index on Users.email; check for duplicate values");
        }

        try
        {
            await db.Prosumers.Indexes.CreateOneAsync(new CreateIndexModel<Prosumer>(
                Builders<Prosumer>.IndexKeys.Ascending(prosumer => prosumer.Nic),
                new CreateIndexOptions { Name = "ux_prosumers_nic", Unique = true }));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not create unique index on Prosumers.nic; check for duplicate values");
        }

        try
        {
            await db.Stations.Indexes.CreateOneAsync(new CreateIndexModel<SolarStationInfo>(
                Builders<SolarStationInfo>.IndexKeys.Ascending(station => station.StationName),
                new CreateIndexOptions { Name = "ux_stations_stationName", Unique = true, Collation = new Collation("en", strength: CollationStrength.Secondary) }));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not create unique index on SolarStationInfo.stationName; check for duplicate values");
        }
    }
}
