/**
 * File: MongoDbService.cs
 * Purpose: Initializes the MongoDB client/database and exposes the Users collection.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SmartMicrogrid.API.Config;
using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public class MongoDbService : IMongoDbService
{
    private readonly IMongoDatabase _database;
    public IMongoCollection<User> Users { get; }
    public IMongoCollection<Prosumer> Prosumers { get; }
    public IMongoCollection<SolarStationInfo> Stations { get; }
    public IMongoCollection<EnergyReservation> Reservations { get; }
    public IMongoCollection<EnergyBookingSlot> Slots { get; }

    // Opens the MongoDB connection and binds the collections used by the application.
    public MongoDbService(IOptions<MongoDbSettings> settings)
    {
        var client = new MongoClient(settings.Value.ConnectionString);
        _database = client.GetDatabase(settings.Value.DatabaseName);
        Users = _database.GetCollection<User>("Users");
        Prosumers = _database.GetCollection<Prosumer>("Prosumers");
        Stations = _database.GetCollection<SolarStationInfo>("SolarStationInfo");
        Reservations = _database.GetCollection<EnergyReservation>("EnergyReservation");
        Slots = _database.GetCollection<EnergyBookingSlot>("EnergyBookingSlots");
    }

    // Executes MongoDB's ping command so health reflects actual database availability.
    public async Task PingAsync()
    {
        await _database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
    }
}
