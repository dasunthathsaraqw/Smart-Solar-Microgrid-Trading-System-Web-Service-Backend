/**
 * File: AuthService.cs
 * Purpose: Implements credential validation against MongoDB and JWT issuance for authenticated users.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using MongoDB.Driver;
using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public class AuthService : IAuthService
{
    private readonly IMongoDbService _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;

    // Initializes authentication with the credential store, password verifier and token issuer.
    public AuthService(IMongoDbService db, IPasswordHasher passwordHasher, IJwtService jwtService)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
    }

    // Validates credentials before reporting inactive accounts, avoiding an email-enumeration signal for wrong passwords.
    public async Task<(LoginResponse? Response, bool AccountInactive)> LoginAsync(LoginRequest request)
    {
        // Exact-match lookup on the Users collection, which holds every login (Backoffice, GridOperator and Prosumer).
        // The comparison is case-sensitive: the Users.email index has no collation.
        var user = await _db.Users.Find(u => u.Email == request.Email).FirstOrDefaultAsync();

        // Unknown email and wrong password share one result, so the response does not reveal which emails exist.
        // (An unknown email skips the BCrypt check, so response time can still differ slightly.)
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return (null, false);
        }

        // Only reached with the correct password, so "inactive" (pending, deactivated) is disclosed only to the account owner. The controller maps it to 403.
        if (!user.IsActive)
        {
            return (null, true);
        }

        var (token, expiresAt) = _jwtService.GenerateToken(user);

        return (new LoginResponse
        {
            Token = token,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role,
            // Station is read from the stored user, not the token, and only operators get one, so the app never sees a stale or foreign assignment.
            StationId = user.Role == "GridOperator" ? user.StationId : null,
            ExpiresAt = expiresAt,
        }, false);
    }

    // Looks up a user by their MongoDB ObjectId string, used to resolve the "current user" from JWT claims.
    public async Task<User?> GetByIdAsync(string id)
    {
        return await _db.Users.Find(u => u.Id == id).FirstOrDefaultAsync();
    }
}
