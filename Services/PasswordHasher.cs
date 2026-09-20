/**
 * File: PasswordHasher.cs
 * Purpose: BCrypt-based implementation of password hashing and verification.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

namespace SmartMicrogrid.API.Services;

public class PasswordHasher : IPasswordHasher
{
    // Hashes a plaintext password using BCrypt with a generated salt.
    public string Hash(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password);
    }

    // Verifies a plaintext password against a previously generated BCrypt hash.
    public bool Verify(string password, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }
}
