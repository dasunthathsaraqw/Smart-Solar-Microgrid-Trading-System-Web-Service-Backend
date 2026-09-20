/**
 * File: DbSeeder.cs
 * Purpose: Seeds the initial Backoffice admin account on first application startup if the Users collection is empty.
 * Author: <Your Name>
 * Date: 2026
 */

using MongoDB.Driver;
using SmartMicrogrid.API.Models;
using SmartMicrogrid.API.Services;

namespace SmartMicrogrid.API.Data;

public static class DbSeeder
{
    // Creates the default "System Admin" Backoffice user when the Users collection is empty.
    public static async Task SeedAsync(IMongoDbService db, IPasswordHasher passwordHasher)
    {
        var hasAnyUser = await db.Users.Find(FilterDefinition<User>.Empty).AnyAsync();
        if (hasAnyUser)
        {
            return;
        }

        var adminUser = new User
        {
            Name = "System Admin",
            Email = "admin@smartsolar.com",
            PasswordHash = passwordHasher.Hash("Admin@123"),
            Role = "Backoffice",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "system",
        };

        await db.Users.InsertOneAsync(adminUser);
    }
}
