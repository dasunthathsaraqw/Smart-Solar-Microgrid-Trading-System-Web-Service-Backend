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
        // BCrypt embeds a random salt and the work factor in the hash string, so no separate salt is stored and hashing the same password twice gives different results.
        return BCrypt.Net.BCrypt.HashPassword(password);
    }

    // Verifies a plaintext password against a previously generated BCrypt hash.
    public bool Verify(string password, string hash)
    {
        // The salt and work factor are read back out of the stored hash, so two hashes of the same password can never be compared as plain strings.
        return BCrypt.Net.BCrypt.Verify(password, hash);
    }
}
