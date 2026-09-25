/**
 * File: JwtService.cs
 * Purpose: Creates HMAC SHA256-signed JWT tokens carrying the user's id, email, name and role claims.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartMicrogrid.API.Config;
using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public class JwtService : IJwtService
{
    private readonly JwtSettings _settings;

    // Initializes token generation from the configured JWT settings.
    public JwtService(IOptions<JwtSettings> settings)
    {
        _settings = settings.Value;
    }

    // Builds a signed JWT containing the user's identity and role claims, valid for the configured expiry window.
    public (string Token, DateTime ExpiresAt) GenerateToken(User user)
    {
        // Expiry comes from configuration. There is no per-request IsActive check in the JWT pipeline (see Program.cs), so a token stays valid until this time
        // even if the account is deactivated afterwards.
        var expiresAt = DateTime.UtcNow.AddMinutes(_settings.ExpiryMinutes);

        // Sub and NameIdentifier carry the same user id: controllers read NameIdentifier, while Sub is the standard JWT subject claim.
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Role, user.Role),
        };

        // Only prosumer accounts have a NIC. The prosumer endpoints trust this signed claim, never client input, to decide whose data to return.
        // No station claim is added for operators: their station is always re-read from the database so reassignment takes effect immediately.
        if (user.Nic is not null)
        {
            claims.Add(new Claim("nic", user.Nic));
        }

        // HMAC-SHA256 with a shared secret; Program.cs refuses to start if the key is missing, still the placeholder, or under 32 characters.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
