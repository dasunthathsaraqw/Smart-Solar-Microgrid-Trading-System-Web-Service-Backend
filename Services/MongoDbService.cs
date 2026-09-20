/**
 * File: MongoDbService.cs
 * Purpose: Initializes the MongoDB client/database and exposes the Users collection.
 * Author: <Your Name>
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

    // Opens the MongoDB connection and binds the collections used by the application.
    public MongoDbService(IOptions<MongoDbSettings> settings)
    {
        var client = new MongoClient(settings.Value.ConnectionString);
        var database = client.GetDatabase(settings.Value.DatabaseName);
        Users = database.GetCollection<User>("Users");
        Prosumers = database.GetCollection<Prosumer>("Prosumers");
    }
}
