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

    // Opens the MongoDB Atlas connection and binds the Users collection using the bound settings.
    public MongoDbService(IOptions<MongoDbSettings> settings)
    {
        var client = new MongoClient(settings.Value.ConnectionString);
        var database = client.GetDatabase(settings.Value.DatabaseName);
        Users = database.GetCollection<User>("Users");
    }
}
