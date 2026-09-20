/**
 * File: MongoDbService.cs
 * Purpose: Initializes the MongoDB client/database and exposes the Users collection.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using Microsoft.Extensions.Options;
using MongoDB.Driver;
using SmartMicrogrid.API.Config;
using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public class MongoDbService : IMongoDbService
{
    public IMongoCollection<User> Users { get; }
    public IMongoCollection<Prosumer> Prosumers { get; }
    public IMongoCollection<SolarStationInfo> Stations { get; }
    public IMongoCollection<EnergyReservation> Reservations { get; }
    public IMongoCollection<EnergyBookingSlot> Slots { get; }

    // Opens the MongoDB connection and binds the collections used by the application.
    public MongoDbService(IOptions<MongoDbSettings> settings)
    {
        var client = new MongoClient(settings.Value.ConnectionString);
        var database = client.GetDatabase(settings.Value.DatabaseName);
        Users = database.GetCollection<User>("Users");
        Prosumers = database.GetCollection<Prosumer>("Prosumers");
        Stations = database.GetCollection<SolarStationInfo>("SolarStationInfo");
        Reservations = database.GetCollection<EnergyReservation>("EnergyReservation");
        Slots = database.GetCollection<EnergyBookingSlot>("EnergyBookingSlots");
    }
}
