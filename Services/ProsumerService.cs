/**
 * File: ProsumerService.cs
 * Purpose: Implements prosumer CRUD and lifecycle rules (NIC/email uniqueness, approval workflow)
 *          against MongoDB. Status is derived, not stored: active | pending | deactivated.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using MongoDB.Driver;
using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public class ProsumerService : IProsumerService
{
    private readonly IMongoDbService _db;
    private readonly IPasswordHasher _passwordHasher;

    // Initializes prosumer operations with MongoDB access and secure password hashing.
    public ProsumerService(IMongoDbService db, IPasswordHasher passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    // Returns prosumers filtered by derived status ("active" | "pending" | "deactivated"), or all when status is null/unknown.
    public async Task<List<ProsumerResponse>> GetAllAsync(string? status)
    {
        FilterDefinition<Prosumer> filter = status?.ToLowerInvariant() switch
        {
            "active" => Builders<Prosumer>.Filter.Eq(p => p.IsActive, true),
            "pending" => Builders<Prosumer>.Filter.And(
                Builders<Prosumer>.Filter.Eq(p => p.IsActive, false),
                Builders<Prosumer>.Filter.Eq(p => p.DeactivationRequested, false)),
            "deactivated" => Builders<Prosumer>.Filter.And(
                Builders<Prosumer>.Filter.Eq(p => p.IsActive, false),
                Builders<Prosumer>.Filter.Eq(p => p.DeactivationRequested, true)),
            _ => FilterDefinition<Prosumer>.Empty,
        };

        var prosumers = await _db.Prosumers.Find(filter).SortByDescending(p => p.CreatedAt).ToListAsync();
        return prosumers.Select(ToResponse).ToList();
    }

    // Looks up a single prosumer by NIC, the prosumer's primary business identifier.
    public async Task<ProsumerResponse?> GetByNicAsync(string nic)
    {
        var prosumer = await _db.Prosumers.Find(p => p.Nic == nic).FirstOrDefaultAsync();
        return prosumer is null ? null : ToResponse(prosumer);
    }

    // Self-registers matching inactive business and credential documents, rolling back an orphaned profile on failure.
    public async Task<ProsumerResponse> RegisterAsync(RegisterProsumerRequest request)
    {
        if (await _db.Prosumers.Find(p => p.Nic == request.Nic).AnyAsync())
        {
            throw new InvalidOperationException("NIC already registered");
        }

        if (await _db.Users.Find(u => u.Email == request.Email).AnyAsync())
        {
            throw new InvalidOperationException("Email already registered");
        }

        var passwordHash = _passwordHasher.Hash(request.Password);
        var createdAt = DateTime.UtcNow;
        var prosumer = new Prosumer
        {
            Nic = request.Nic,
            Name = request.Name,
            Email = request.Email,
            ContactNumber = request.ContactNumber,
            Address = request.Address,
            PanelCapacityKw = request.PanelCapacityKw,
            PasswordHash = passwordHash,
            IsActive = false,
            DeactivationRequested = false,
            CreatedAt = createdAt,
            CreatedBy = "self-registration",
        };

        var user = new User
        {
            Nic = request.Nic,
            Name = request.Name,
            Email = request.Email,
            PasswordHash = passwordHash,
            Role = "Prosumer",
            IsActive = false,
            CreatedAt = createdAt,
            CreatedBy = "self-registration",
        };

        await _db.Prosumers.InsertOneAsync(prosumer);

        try
        {
            await _db.Users.InsertOneAsync(user);
        }
        catch
        {
            await _db.Prosumers.DeleteOneAsync(p => p.Id == prosumer.Id);
            throw;
        }

        return ToResponse(prosumer);
    }

    // Creates a new prosumer, enforcing NIC/email uniqueness. New prosumers start pending approval.
    public async Task<ProsumerResponse> CreateAsync(CreateProsumerRequest request, string createdBy)
    {
        if (await NicExistsAsync(request.Nic))
        {
            throw new InvalidOperationException("NIC already exists");
        }

        if (await EmailExistsAsync(request.Email))
        {
            throw new InvalidOperationException("Email already exists");
        }

        var prosumer = new Prosumer
        {
            Nic = request.Nic,
            Name = request.Name,
            Email = request.Email,
            ContactNumber = request.ContactNumber,
            Address = request.Address,
            PanelCapacityKw = request.PanelCapacityKw,
            PasswordHash = _passwordHasher.Hash(request.Password),
            IsActive = false,
            DeactivationRequested = false,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
        };

        await _db.Prosumers.InsertOneAsync(prosumer);
        return ToResponse(prosumer);
    }

    // Updates the editable fields of a prosumer (never the NIC). Re-checks email uniqueness if it changed.
    public async Task<ProsumerResponse?> UpdateAsync(string nic, UpdateProsumerRequest request)
    {
        var prosumer = await _db.Prosumers.Find(p => p.Nic == nic).FirstOrDefaultAsync();
        if (prosumer is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.Email) && request.Email != prosumer.Email && await EmailExistsAsync(request.Email))
        {
            throw new InvalidOperationException("Email already exists");
        }

        var updates = new List<UpdateDefinition<Prosumer>>();

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            updates.Add(Builders<Prosumer>.Update.Set(p => p.Name, request.Name));
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            updates.Add(Builders<Prosumer>.Update.Set(p => p.Email, request.Email));
        }

        if (!string.IsNullOrWhiteSpace(request.ContactNumber))
        {
            updates.Add(Builders<Prosumer>.Update.Set(p => p.ContactNumber, request.ContactNumber));
        }

        if (!string.IsNullOrWhiteSpace(request.Address))
        {
            updates.Add(Builders<Prosumer>.Update.Set(p => p.Address, request.Address));
        }

        if (request.PanelCapacityKw.HasValue)
        {
            updates.Add(Builders<Prosumer>.Update.Set(p => p.PanelCapacityKw, request.PanelCapacityKw.Value));
        }

        if (updates.Count > 0)
        {
            updates.Add(Builders<Prosumer>.Update.Set(p => p.UpdatedAt, DateTime.UtcNow));
            await _db.Prosumers.UpdateOneAsync(p => p.Nic == nic, Builders<Prosumer>.Update.Combine(updates));
        }

        var updated = await _db.Prosumers.Find(p => p.Nic == nic).FirstOrDefaultAsync();
        return ToResponse(updated!);
    }

    // Updates only the authenticated prosumer's editable fields and keeps their credential identity in sync.
    public async Task<ProsumerResponse?> UpdateOwnProfileAsync(string nic, UpdateOwnProfileRequest request)
    {
        var prosumer = await _db.Prosumers.Find(p => p.Nic == nic).FirstOrDefaultAsync();
        if (prosumer is null)
        {
            return null;
        }

        var user = await _db.Users.Find(u => u.Nic == nic).FirstOrDefaultAsync();
        if (user is null)
        {
            throw new InvalidOperationException("Credential account not found");
        }

        if (request.Email is not null && request.Email != prosumer.Email &&
            await _db.Users.Find(u => u.Email == request.Email && u.Id != user.Id).AnyAsync())
        {
            throw new InvalidOperationException("Email already registered");
        }

        var prosumerUpdates = new List<UpdateDefinition<Prosumer>>();
        var userUpdates = new List<UpdateDefinition<User>>();

        if (request.Name is not null)
        {
            prosumerUpdates.Add(Builders<Prosumer>.Update.Set(p => p.Name, request.Name));
            userUpdates.Add(Builders<User>.Update.Set(u => u.Name, request.Name));
        }

        if (request.Email is not null)
        {
            prosumerUpdates.Add(Builders<Prosumer>.Update.Set(p => p.Email, request.Email));
            userUpdates.Add(Builders<User>.Update.Set(u => u.Email, request.Email));
        }

        if (request.ContactNumber is not null)
        {
            prosumerUpdates.Add(Builders<Prosumer>.Update.Set(p => p.ContactNumber, request.ContactNumber));
        }

        if (request.Address is not null)
        {
            prosumerUpdates.Add(Builders<Prosumer>.Update.Set(p => p.Address, request.Address));
        }

        if (request.PanelCapacityKw.HasValue)
        {
            prosumerUpdates.Add(Builders<Prosumer>.Update.Set(p => p.PanelCapacityKw, request.PanelCapacityKw.Value));
        }

        if (prosumerUpdates.Count > 0)
        {
            var updatedAt = DateTime.UtcNow;
            prosumerUpdates.Add(Builders<Prosumer>.Update.Set(p => p.UpdatedAt, updatedAt));
            await _db.Prosumers.UpdateOneAsync(p => p.Nic == nic, Builders<Prosumer>.Update.Combine(prosumerUpdates));

            if (userUpdates.Count > 0)
            {
                userUpdates.Add(Builders<User>.Update.Set(u => u.UpdatedAt, updatedAt));
                await _db.Users.UpdateOneAsync(u => u.Id == user.Id, Builders<User>.Update.Combine(userUpdates));
            }
        }

        var updated = await _db.Prosumers.Find(p => p.Nic == nic).FirstOrDefaultAsync();
        return ToResponse(updated!);
    }

    // Verifies the current password and writes one newly hashed password to both synchronized documents.
    public async Task<bool> ChangePasswordAsync(string nic, ChangePasswordRequest request)
    {
        var user = await _db.Users.Find(u => u.Nic == nic).FirstOrDefaultAsync();
        var prosumer = await _db.Prosumers.Find(p => p.Nic == nic).FirstOrDefaultAsync();
        if (user is null || prosumer is null || !_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return false;
        }

        var passwordHash = _passwordHasher.Hash(request.NewPassword);
        var updatedAt = DateTime.UtcNow;

        await _db.Prosumers.UpdateOneAsync(
            p => p.Nic == nic,
            Builders<Prosumer>.Update
                .Set(p => p.PasswordHash, passwordHash)
                .Set(p => p.UpdatedAt, updatedAt));

        try
        {
            await _db.Users.UpdateOneAsync(
                u => u.Id == user.Id,
                Builders<User>.Update
                    .Set(u => u.PasswordHash, passwordHash)
                    .Set(u => u.UpdatedAt, updatedAt));
        }
        catch
        {
            await _db.Prosumers.UpdateOneAsync(
                p => p.Nic == nic,
                Builders<Prosumer>.Update
                    .Set(p => p.PasswordHash, prosumer.PasswordHash)
                    .Set(p => p.UpdatedAt, prosumer.UpdatedAt));
            throw;
        }

        return true;
    }

    // Records a deactivation request only when the active prosumer has no pending or approved reservations.
    public async Task<(bool Success, string? Error)> RequestDeactivationAsync(string nic)
    {
        var prosumer = await _db.Prosumers.Find(p => p.Nic == nic).FirstOrDefaultAsync();
        if (prosumer is null)
        {
            return (false, "Prosumer not found.");
        }

        if (!prosumer.IsActive)
        {
            return (false, "The prosumer account is already inactive.");
        }

        var hasOpenReservation = await _db.Reservations.Find(r =>
            r.ProsumerNic == nic && (r.Status == "Pending" || r.Status == "Approved")).AnyAsync();
        if (hasOpenReservation)
        {
            return (false, "Cancel all pending or approved reservations before requesting deactivation.");
        }

        await _db.Prosumers.UpdateOneAsync(
            p => p.Nic == nic,
            Builders<Prosumer>.Update
                .Set(p => p.DeactivationRequested, true)
                .Set(p => p.UpdatedAt, DateTime.UtcNow));

        return (true, null);
    }

    // Returns active prosumers whose self-service deactivation requests await Backoffice action.
    public async Task<List<ProsumerResponse>> GetPendingDeactivationsAsync()
    {
        var prosumers = await _db.Prosumers
            .Find(p => p.IsActive && p.DeactivationRequested)
            .SortByDescending(p => p.UpdatedAt)
            .ToListAsync();

        return prosumers.Select(ToResponse).ToList();
    }

    // Deactivates a prosumer, clears the fulfilled request and disables the matching login account.
    public async Task<bool> DeactivateAsync(string nic)
    {
        var result = await _db.Prosumers.UpdateOneAsync(
            p => p.Nic == nic,
            Builders<Prosumer>.Update
                .Set(p => p.IsActive, false)
                .Set(p => p.DeactivationRequested, false)
                .Set(p => p.UpdatedAt, DateTime.UtcNow)
        );

        if (result.MatchedCount > 0)
        {
            await _db.Users.UpdateOneAsync(
                u => u.Nic == nic,
                Builders<User>.Update
                    .Set(u => u.IsActive, false)
                    .Set(u => u.UpdatedAt, DateTime.UtcNow));
        }

        return result.MatchedCount > 0;
    }

    // Reactivates or approves a prosumer and enables the matching login account.
    public async Task<bool> ReactivateAsync(string nic)
    {
        var result = await _db.Prosumers.UpdateOneAsync(
            p => p.Nic == nic,
            Builders<Prosumer>.Update
                .Set(p => p.IsActive, true)
                .Set(p => p.DeactivationRequested, false)
                .Set(p => p.UpdatedAt, DateTime.UtcNow)
        );

        if (result.MatchedCount > 0)
        {
            await _db.Users.UpdateOneAsync(
                u => u.Nic == nic,
                Builders<User>.Update
                    .Set(u => u.IsActive, true)
                    .Set(u => u.UpdatedAt, DateTime.UtcNow));
        }

        return result.MatchedCount > 0;
    }

    // Checks whether a prosumer with the given NIC already exists.
    public async Task<bool> NicExistsAsync(string nic)
    {
        return await _db.Prosumers.Find(p => p.Nic == nic).AnyAsync();
    }

    // Checks whether a prosumer with the given email already exists.
    public async Task<bool> EmailExistsAsync(string email)
    {
        return await _db.Prosumers.Find(p => p.Email == email).AnyAsync();
    }

    // Maps a Prosumer document to its public response shape, omitting the password hash and deriving Status.
    private static ProsumerResponse ToResponse(Prosumer prosumer)
    {
        var status = prosumer.IsActive ? "active" : prosumer.DeactivationRequested ? "deactivated" : "pending";

        return new ProsumerResponse
        {
            Id = prosumer.Id,
            Nic = prosumer.Nic,
            Name = prosumer.Name,
            Email = prosumer.Email,
            ContactNumber = prosumer.ContactNumber,
            Address = prosumer.Address,
            PanelCapacityKw = prosumer.PanelCapacityKw,
            IsActive = prosumer.IsActive,
            DeactivationRequested = prosumer.DeactivationRequested,
            CreatedAt = prosumer.CreatedAt,
            CreatedBy = prosumer.CreatedBy,
            UpdatedAt = prosumer.UpdatedAt,
            Status = status,
        };
    }
}
