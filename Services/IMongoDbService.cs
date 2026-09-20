/**
 * File: IMongoDbService.cs
 * Purpose: Contract exposing typed access to the MongoDB collections used by the application.
 * Author: <Your Name>
 * Date: 2026
 */

using MongoDB.Driver;
using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public interface IMongoDbService
{
    IMongoCollection<User> Users { get; }
    IMongoCollection<Prosumer> Prosumers { get; }
    IMongoCollection<SolarStationInfo> Stations { get; }
    IMongoCollection<EnergyReservation> Reservations { get; }
    IMongoCollection<EnergyBookingSlot> Slots { get; }
}
