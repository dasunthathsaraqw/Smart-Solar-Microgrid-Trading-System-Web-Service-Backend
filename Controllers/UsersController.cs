/**
 * File: UsersController.cs
 * Purpose: Backoffice-only user management endpoints (create, list, get, update, soft-delete).
 * Author: <Your Name>
 * Date: 2026
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using SmartMicrogrid.API.Models;
using SmartMicrogrid.API.Services;

namespace SmartMicrogrid.API.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Backoffice")]
public class UsersController : ControllerBase
{
    private readonly IMongoDbService _db;
    private readonly IPasswordHasher _passwordHasher;

    public UsersController(IMongoDbService db, IPasswordHasher passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    // Handles POST /api/users — creates a new Backoffice or GridOperator account.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var emailExists = await _db.Users.Find(u => u.Email == request.Email).AnyAsync();
        if (emailExists)
        {
            return Conflict(new { message = "A user with this email already exists." });
        }

        var creatorEmail = User.Identity?.Name;

        var user = new User
        {
            Name = request.Name,
            Email = request.Email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            Role = request.Role,
            Nic = request.Nic,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = creatorEmail,
        };

        await _db.Users.InsertOneAsync(user);

        return CreatedAtAction(nameof(GetById), new { id = user.Id }, ToSafeResponse(user));
    }

    // Handles GET /api/users — lists all users, excluding password hashes.
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = await _db.Users.Find(FilterDefinition<User>.Empty).ToListAsync();
        return Ok(users.Select(ToSafeResponse));
    }

    // Handles GET /api/users/{id} — returns a single user, excluding the password hash.
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var user = await _db.Users.Find(u => u.Id == id).FirstOrDefaultAsync();
        if (user is null)
        {
            return NotFound();
        }

        return Ok(ToSafeResponse(user));
    }

    // Handles PUT /api/users/{id} — updates name, email, password and/or active status.
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateUserRequest request)
    {
        var user = await _db.Users.Find(u => u.Id == id).FirstOrDefaultAsync();
        if (user is null)
        {
            return NotFound();
        }

        var updates = new List<UpdateDefinition<User>>();

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            updates.Add(Builders<User>.Update.Set(u => u.Name, request.Name));
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            updates.Add(Builders<User>.Update.Set(u => u.Email, request.Email));
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            updates.Add(Builders<User>.Update.Set(u => u.PasswordHash, _passwordHasher.Hash(request.Password)));
        }

        if (request.IsActive.HasValue)
        {
            updates.Add(Builders<User>.Update.Set(u => u.IsActive, request.IsActive.Value));
        }

        if (updates.Count == 0)
        {
            return Ok(ToSafeResponse(user));
        }

        await _db.Users.UpdateOneAsync(u => u.Id == id, Builders<User>.Update.Combine(updates));

        var updated = await _db.Users.Find(u => u.Id == id).FirstOrDefaultAsync();
        return Ok(ToSafeResponse(updated!));
    }

    // Handles DELETE /api/users/{id} — soft-deletes a user by setting IsActive to false.
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var result = await _db.Users.UpdateOneAsync(
            u => u.Id == id,
            Builders<User>.Update.Set(u => u.IsActive, false)
        );

        if (result.MatchedCount == 0)
        {
            return NotFound();
        }

        return NoContent();
    }

    // Strips the password hash before returning a user to the client.
    private static object ToSafeResponse(User user)
    {
        return new
        {
            user.Id,
            user.Nic,
            user.Name,
            user.Email,
            user.Role,
            user.IsActive,
            user.CreatedAt,
            user.CreatedBy,
        };
    }
}
