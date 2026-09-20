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

    // Deactivates a prosumer: IsActive=false and DeactivationRequested=true so it is reported as "deactivated", not "pending".
    public async Task<bool> DeactivateAsync(string nic)
    {
        var result = await _db.Prosumers.UpdateOneAsync(
            p => p.Nic == nic,
            Builders<Prosumer>.Update
                .Set(p => p.IsActive, false)
                .Set(p => p.DeactivationRequested, true)
                .Set(p => p.UpdatedAt, DateTime.UtcNow)
        );

        return result.MatchedCount > 0;
    }

    // Reactivates (or approves) a prosumer: IsActive=true and DeactivationRequested reset to false.
    public async Task<bool> ReactivateAsync(string nic)
    {
        var result = await _db.Prosumers.UpdateOneAsync(
            p => p.Nic == nic,
            Builders<Prosumer>.Update
                .Set(p => p.IsActive, true)
                .Set(p => p.DeactivationRequested, false)
                .Set(p => p.UpdatedAt, DateTime.UtcNow)
        );

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
