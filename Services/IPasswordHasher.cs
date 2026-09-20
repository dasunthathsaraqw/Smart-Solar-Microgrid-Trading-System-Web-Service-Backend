/**
 * File: IPasswordHasher.cs
 * Purpose: Contract for hashing and verifying user passwords.
 * Author: <Your Name>
 * Date: 2026
 */

namespace SmartMicrogrid.API.Services;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
