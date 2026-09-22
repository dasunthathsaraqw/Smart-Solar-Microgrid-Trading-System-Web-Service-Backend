/**
 * File: MongoIndexSeeder.cs
 * Purpose: Adds database indexes that enforce uniqueness and optimize application query patterns.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using MongoDB.Bson;
using MongoDB.Driver;
using SmartMicrogrid.API.Services;
using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Data;

public static class MongoIndexSeeder
{
    // Creates each index independently so a conflict in one collection does not block others.
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

        try
        {
            await db.Reservations.Indexes.CreateOneAsync(new CreateIndexModel<EnergyReservation>(
                Builders<EnergyReservation>.IndexKeys.Ascending(reservation => reservation.QrToken),
                new CreateIndexOptions<EnergyReservation>
                {
                    Name = "ux_reservations_qrToken_string",
                    Unique = true,
                    PartialFilterExpression = Builders<EnergyReservation>.Filter.Type(
                        reservation => reservation.QrToken,
                        BsonType.String),
                }));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not create unique partial index on EnergyReservation.qrToken; check for duplicate string values");
        }

        try
        {
            var keys = Builders<EnergyReservation>.IndexKeys
                .Ascending(reservation => reservation.Status)
                .Descending(reservation => reservation.CompletedAt);
            await db.Reservations.Indexes.CreateOneAsync(new CreateIndexModel<EnergyReservation>(
                keys,
                new CreateIndexOptions { Name = "ix_reservations_status_completedAt" }));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not create EnergyReservation status/completedAt index");
        }

        try
        {
            var keys = Builders<EnergyReservation>.IndexKeys
                .Ascending(reservation => reservation.StationId)
                .Ascending(reservation => reservation.Status)
                .Descending(reservation => reservation.CompletedAt);
            await db.Reservations.Indexes.CreateOneAsync(new CreateIndexModel<EnergyReservation>(
                keys,
                new CreateIndexOptions { Name = "ix_reservations_stationId_status_completedAt" }));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not create EnergyReservation stationId/status/completedAt index");
        }

        try
        {
            var keys = Builders<EnergyReservation>.IndexKeys
                .Ascending(reservation => reservation.Status)
                .Ascending(reservation => reservation.SlotStartTime);
            await db.Reservations.Indexes.CreateOneAsync(new CreateIndexModel<EnergyReservation>(
                keys,
                new CreateIndexOptions { Name = "ix_reservations_status_slotStartTime" }));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not create EnergyReservation status/slotStartTime index");
        }

        try
        {
            var keys = Builders<EnergyReservation>.IndexKeys
                .Ascending(reservation => reservation.StationId)
                .Ascending(reservation => reservation.Status)
                .Ascending(reservation => reservation.SlotStartTime);
            await db.Reservations.Indexes.CreateOneAsync(new CreateIndexModel<EnergyReservation>(
                keys,
                new CreateIndexOptions { Name = "ix_reservations_stationId_status_slotStartTime" }));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not create EnergyReservation stationId/status/slotStartTime index");
        }
    }
}
