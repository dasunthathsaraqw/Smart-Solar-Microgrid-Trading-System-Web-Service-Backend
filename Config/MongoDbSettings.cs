/**
 * File: MongoDbSettings.cs
 * Purpose: Strongly-typed binding for the MongoDB configuration section (connection string, database name).
 * Author: <Your Name>
 * Date: 2026
 */

namespace SmartMicrogrid.API.Config;

public class MongoDbSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
}
