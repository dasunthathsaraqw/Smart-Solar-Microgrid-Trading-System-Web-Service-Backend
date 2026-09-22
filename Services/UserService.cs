/**
 * File: UserService.cs
 * Purpose: Implements Backoffice/GridOperator user CRUD against MongoDB, enforcing email
 *          uniqueness, the default-admin email protection, and the deactivation safety rules
 *          (cannot deactivate yourself, cannot deactivate the last active Backoffice).
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public class UserService : IUserService
{
    // The seeded default Backoffice account (see Data/DbSeeder.cs) — its email is protected from change.
    private const string SeedAdminEmail = "admin@smartsolar.com";

    private readonly IMongoDbService _db;
    private readonly IPasswordHasher _passwordHasher;

    // Initializes user operations with database access and password hashing.
    public UserService(IMongoDbService db, IPasswordHasher passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    // Returns users optionally filtered by role ("Backoffice" | "GridOperator") and status ("active" | "deactivated").
    public async Task<List<UserResponse>> GetAllAsync(string? role, string? status)
    {
        var filters = new List<FilterDefinition<User>>();

        if (!string.IsNullOrEmpty(role))
        {
            filters.Add(Builders<User>.Filter.Eq(u => u.Role, role));
        }

        switch (status?.ToLowerInvariant())
        {
            case "active":
                filters.Add(Builders<User>.Filter.Eq(u => u.IsActive, true));
                break;
            case "deactivated":
                filters.Add(Builders<User>.Filter.Eq(u => u.IsActive, false));
                break;
        }

        var filter = filters.Count > 0 ? Builders<User>.Filter.And(filters) : FilterDefinition<User>.Empty;
        var users = await _db.Users.Find(filter).SortByDescending(u => u.CreatedAt).ToListAsync();
        return users.Select(ToResponse).ToList();
    }

    // Looks up a single user by their MongoDB ObjectId.
    public async Task<UserResponse?> GetByIdAsync(string id)
    {
        var user = await _db.Users.Find(u => u.Id == id).FirstOrDefaultAsync();
        return user is null ? null : ToResponse(user);
    }

    // Looks up a single user by email (case-insensitive).
    public async Task<UserResponse?> GetByEmailAsync(string email)
    {
        var user = await _db.Users.Find(u => u.Email == email).FirstOrDefaultAsync();
        return user is null ? null : ToResponse(user);
    }

    // Creates a new Backoffice or GridOperator user, enforcing email uniqueness and optional operator station validity.
    public async Task<UserResponse> CreateAsync(CreateUserRequest request, string createdByEmail)
    {
        if (await EmailExistsAsync(request.Email))
        {
            throw new InvalidOperationException("Email already exists");
        }

        var stationId = request.Role == "GridOperator" && !string.IsNullOrWhiteSpace(request.StationId)
            ? await ValidateStationIdAsync(request.StationId)
            : null;

        var user = new User
        {
            Name = request.Name,
            Email = request.Email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            Role = request.Role,
            StationId = stationId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdByEmail,
        };

        await _db.Users.InsertOneAsync(user);
        return ToResponse(user);
    }

    // Updates the editable fields of a user. Re-checks email uniqueness if it changed and
    // protects the seeded default admin account's email from being changed.
    public async Task<UserResponse?> UpdateAsync(string id, UpdateUserRequest request, string updatedByEmail)
    {
        var user = await _db.Users.Find(u => u.Id == id).FirstOrDefaultAsync();
        if (user is null)
        {
            return null;
        }

        var emailChanging = !string.IsNullOrWhiteSpace(request.Email)
            && !string.Equals(request.Email, user.Email, StringComparison.OrdinalIgnoreCase);

        if (emailChanging && string.Equals(user.Email, SeedAdminEmail, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Cannot change the email of the default admin account");
        }

        if (emailChanging && await EmailExistsAsync(request.Email!, id))
        {
            throw new InvalidOperationException("Email already exists");
        }

        var updates = new List<UpdateDefinition<User>>();

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            updates.Add(Builders<User>.Update.Set(u => u.Name, request.Name));
        }

        if (emailChanging)
        {
            updates.Add(Builders<User>.Update.Set(u => u.Email, request.Email));
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            updates.Add(Builders<User>.Update.Set(u => u.PasswordHash, _passwordHasher.Hash(request.Password)));
        }

        var resultingRole = !string.IsNullOrWhiteSpace(request.Role) ? request.Role : user.Role;
        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            updates.Add(Builders<User>.Update.Set(u => u.Role, request.Role));
        }

        if (resultingRole != "GridOperator" && user.StationId is not null)
        {
            updates.Add(Builders<User>.Update.Unset(u => u.StationId));
        }
        else if (request.StationIdSpecified)
        {
            if (string.IsNullOrWhiteSpace(request.StationId))
            {
                updates.Add(Builders<User>.Update.Unset(u => u.StationId));
            }
            else
            {
                var stationId = await ValidateStationIdAsync(request.StationId);
                updates.Add(Builders<User>.Update.Set(u => u.StationId, stationId));
            }
        }

        if (request.IsActive.HasValue)
        {
            updates.Add(Builders<User>.Update.Set(u => u.IsActive, request.IsActive.Value));
        }

        if (updates.Count > 0)
        {
            updates.Add(Builders<User>.Update.Set(u => u.UpdatedAt, DateTime.UtcNow));
            await _db.Users.UpdateOneAsync(u => u.Id == id, Builders<User>.Update.Combine(updates));
        }

        var updated = await _db.Users.Find(u => u.Id == id).FirstOrDefaultAsync();
        return ToResponse(updated!);
    }

    // Deactivates a user unless they are deactivating themselves, or they are the last active Backoffice.
    public async Task<(bool Success, string? Error)> DeactivateAsync(string id, string requestingUserId)
    {
        var user = await _db.Users.Find(u => u.Id == id).FirstOrDefaultAsync();
        if (user is null)
        {
            return (false, "User not found");
        }

        if (string.Equals(id, requestingUserId, StringComparison.Ordinal))
        {
            return (false, "You cannot deactivate your own account");
        }

        if (user.Role == "Backoffice" && user.IsActive && await CountActiveBackofficesAsync() <= 1)
        {
            return (false, "Cannot deactivate the last active Backoffice user");
        }

        await _db.Users.UpdateOneAsync(
            u => u.Id == id,
            Builders<User>.Update.Set(u => u.IsActive, false).Set(u => u.UpdatedAt, DateTime.UtcNow)
        );

        return (true, null);
    }

    // Reactivates a previously deactivated user.
    public async Task<bool> ReactivateAsync(string id)
    {
        var result = await _db.Users.UpdateOneAsync(
            u => u.Id == id,
            Builders<User>.Update.Set(u => u.IsActive, true).Set(u => u.UpdatedAt, DateTime.UtcNow)
        );

        return result.MatchedCount > 0;
    }

    // Checks whether an email is already in use (case-insensitive), optionally excluding one user's own id.
    public async Task<bool> EmailExistsAsync(string email, string? excludeId = null)
    {
        var emailFilter = Builders<User>.Filter.Regex(
            u => u.Email,
            new BsonRegularExpression($"^{Regex.Escape(email)}$", "i")
        );

        var filter = string.IsNullOrEmpty(excludeId)
            ? emailFilter
            : Builders<User>.Filter.And(emailFilter, Builders<User>.Filter.Ne(u => u.Id, excludeId));

        return await _db.Users.Find(filter).AnyAsync();
    }

    // Counts how many Backoffice users are currently active, used by the last-Backoffice safety rule.
    public async Task<int> CountActiveBackofficesAsync()
    {
        return (int)await _db.Users.CountDocumentsAsync(u => u.Role == "Backoffice" && u.IsActive);
    }

    // Resolves an operator's persisted station and rejects unassigned or foreign requested scopes.
    public async Task<(bool UserExists, string? StationId, string? Error)> ResolveOperatorStationAsync(
        string operatorId,
        string? requestedStationId)
    {
        var user = await GetByIdAsync(operatorId);
        if (user is null)
        {
            return (false, null, "The authenticated operator account no longer exists.");
        }

        if (string.IsNullOrWhiteSpace(user.StationId))
        {
            return (true, null, "Grid Operator is not assigned to a station.");
        }

        if (!string.IsNullOrWhiteSpace(requestedStationId) &&
            !string.Equals(user.StationId, requestedStationId, StringComparison.Ordinal))
        {
            return (true, null, "Grid Operator is not assigned to the requested station.");
        }

        return (true, user.StationId, null);
    }

    // Validates an operator station reference as a MongoDB ObjectId that identifies an existing station.
    private async Task<string> ValidateStationIdAsync(string stationId)
    {
        if (!ObjectId.TryParse(stationId, out _))
        {
            throw new InvalidOperationException("Invalid station ID");
        }

        if (!await _db.Stations.Find(station => station.Id == stationId).AnyAsync())
        {
            throw new InvalidOperationException("Station not found");
        }

        return stationId;
    }

    // Maps a User document to its public response shape, omitting the password hash.
    private static UserResponse ToResponse(User user)
    {
        return new UserResponse
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role,
            StationId = user.StationId,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            CreatedBy = user.CreatedBy,
            UpdatedAt = user.UpdatedAt,
        };
    }
}
