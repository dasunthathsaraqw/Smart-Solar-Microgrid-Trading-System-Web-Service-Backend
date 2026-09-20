/**
 * File: JwtSettings.cs
 * Purpose: Strongly-typed binding for the JWT configuration section (signing key, issuer, audience, expiry).
 * Author: <Your Name>
 * Date: 2026
 */

namespace SmartMicrogrid.API.Config;

public class JwtSettings
{
    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 60;
}
