/**
 * File: IPasswordHasher.cs
 * Purpose: Contract for hashing and verifying user passwords.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

namespace SmartMicrogrid.API.Services;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
