/**
 * File: SampleDataSeeder.cs
 * Purpose: Opt-in Sri Lankan demo stations, slots, accounts and reservation lifecycle examples.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using MongoDB.Driver;
using SmartMicrogrid.API.Models;
using SmartMicrogrid.API.Services;

namespace SmartMicrogrid.API.Data;

public static class SampleDataSeeder
{
    // Seeds a complete demo dataset once, and only when the station collection is empty.
    public static async Task SeedAsync(IMongoDbService db, IPasswordHasher passwordHasher)
    {
        if (await db.Stations.Find(FilterDefinition<SolarStationInfo>.Empty).AnyAsync())
        {
            return;
        }

        var now = DateTime.UtcNow;
        var operatorUser = new User
        {
            Name = "Demo Grid Operator",
            Email = "operator@smartsolar.com",
            PasswordHash = passwordHasher.Hash("Operator@123"),
            Role = "GridOperator",
            IsActive = true,
            CreatedAt = now,
            CreatedBy = "sample-data",
        };
        await db.Users.InsertOneAsync(operatorUser);

        var stationDetails = new (string Name, double Latitude, double Longitude, double CapacityKw, bool IsActive)[]
        {
            ("Colombo Fort Solar Hub", 6.9344, 79.8428, 120, true),
            ("Colombo Dehiwala Solar Hub", 6.8510, 79.8650, 90, true),
            ("Gampaha Solar Hub", 7.0873, 79.9990, 75, true),
            ("Negombo Solar Hub", 7.2083, 79.8358, 100, true),
            ("Kandy Solar Hub", 7.2906, 80.6337, 110, true),
            ("Galle Fort Solar Hub", 6.0329, 80.2168, 85, true),
            ("Colombo Retired Solar Hub", 6.9271, 79.8612, 40, false),
        };
        var stations = stationDetails.Select(detail => new SolarStationInfo
        {
            StationName = detail.Name,
            Latitude = detail.Latitude,
            Longitude = detail.Longitude,
            CapacityKw = detail.CapacityKw,
            AvailableSlots = detail.IsActive ? 21 : 0,
            Schedule = "Daily 09:00-17:00 Sri Lanka time",
            IsActive = detail.IsActive,
            CreatedAt = now,
            CreatedBy = "sample-data",
        }).ToList();
        await db.Stations.InsertManyAsync(stations);

        var prosumerDetails = new (string Nic, string Name, string Email, bool Active, bool DeactivationRequested)[]
        {
            ("200012345678", "Demo Prosumer One", "prosumer1@smartsolar.com", true, false),
            ("200112345678", "Demo Prosumer Two", "prosumer2@smartsolar.com", true, false),
            ("200212345678", "Demo Prosumer Three", "prosumer3@smartsolar.com", true, false),
            ("200312345678", "Pending Demo Prosumer", "prosumer4@smartsolar.com", false, false),
            ("200412345678", "Deactivated Demo Prosumer", "prosumer5@smartsolar.com", false, true),
        };
        var prosumers = new List<Prosumer>();
        var users = new List<User>();
        foreach (var detail in prosumerDetails)
        {
            var hash = passwordHasher.Hash("Prosumer@123");
            prosumers.Add(new Prosumer
            {
                Nic = detail.Nic,
                Name = detail.Name,
                Email = detail.Email,
                ContactNumber = "0771234567",
                Address = "Sri Lanka",
                PanelCapacityKw = 10,
                PasswordHash = hash,
                IsActive = detail.Active,
                DeactivationRequested = detail.DeactivationRequested,
                CreatedAt = now,
                CreatedBy = "sample-data",
            });
            users.Add(new User
            {
                Nic = detail.Nic,
                Name = detail.Name,
                Email = detail.Email,
                PasswordHash = hash,
                Role = "Prosumer",
                IsActive = detail.Active,
                CreatedAt = now,
                CreatedBy = "sample-data",
            });
        }
        await db.Prosumers.InsertManyAsync(prosumers);
        await db.Users.InsertManyAsync(users);

        var slots = new List<EnergyBookingSlot>();
        foreach (var station in stations.Where(station => station.IsActive))
        {
            for (var day = 1; day <= 7; day++)
            {
                var date = now.Date.AddDays(day);
                foreach (var startHourUtc in new[] { 3.5, 5.5, 8.5 })
                {
                    var start = date.AddHours(startHourUtc);
                    slots.Add(new EnergyBookingSlot
                    {
                        StationId = station.Id,
                        StationName = station.StationName,
                        SlotDate = date,
                        StartTime = start,
                        EndTime = start.AddHours(1),
                        CapacityKw = Math.Min(10, station.CapacityKw),
                        CreatedAt = now,
                        CreatedBy = "sample-data",
                    });
                }
            }
        }
        var historicalSlot = new EnergyBookingSlot
        {
            StationId = stations[0].Id,
            StationName = stations[0].StationName,
            SlotDate = now.Date.AddDays(-1),
            StartTime = now.Date.AddDays(-1).AddHours(3.5),
            EndTime = now.Date.AddDays(-1).AddHours(4.5),
            CapacityKw = 10,
            CreatedAt = now.AddDays(-2),
            CreatedBy = "sample-data",
        };
        slots.Add(historicalSlot);
        slots[0].IsBooked = true;
        slots[1].IsBooked = true;
        await db.Slots.InsertManyAsync(slots);

        var reservationSlots = new[] { slots[0], slots[1], historicalSlot, slots[2] };
        var statuses = new[] { "Pending", "Approved", "Completed", "Cancelled" };
        var reservations = new List<EnergyReservation>();
        for (var index = 0; index < statuses.Length; index++)
        {
            var slot = reservationSlots[index];
            var prosumer = prosumers[index % 3];
            var status = statuses[index];
            reservations.Add(new EnergyReservation
            {
                ProsumerNic = prosumer.Nic,
                ProsumerName = prosumer.Name,
                StationId = slot.StationId,
                StationName = slot.StationName,
                SlotId = slot.Id,
                SlotStartTime = slot.StartTime,
                SlotEndTime = slot.EndTime,
                CapacityKw = slot.CapacityKw,
                Status = status,
                QrToken = status == "Approved" ? ReservationService.GenerateQrToken() : null,
                QrGeneratedAt = status == "Approved" ? now : null,
                ApprovedAt = status == "Approved" ? now.AddHours(-1) : status == "Completed" ? now.AddDays(-2).AddHours(1) : null,
                ApprovedBy = status is "Approved" or "Completed" ? operatorUser.Email : null,
                CompletedAt = status == "Completed" ? slot.EndTime.AddHours(1) : null,
                CompletedBy = status == "Completed" ? operatorUser.Email : null,
                CancelledAt = status == "Cancelled" ? now : null,
                CancelledBy = status == "Cancelled" ? prosumer.Email : null,
                CreatedAt = status == "Completed" ? now.AddDays(-2) : now.AddHours(-2),
                CreatedBy = "sample-data",
            });
        }
        await db.Reservations.InsertManyAsync(reservations);
    }
}
