/**
 * File: SlotAvailabilityTests.cs
 * Purpose: Integration checks for seven-day slot visibility and prosumer management restrictions.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using System.Net;
using System.Net.Http.Json;
using SmartMicrogrid.API.Models;
using SmartMicrogrid.API.Tests.Infrastructure;
using Xunit;

namespace SmartMicrogrid.API.Tests;

[Collection("Api")]
public sealed class SlotAvailabilityTests
{
    private readonly ApiFactory _factory;
    private readonly TestHelpers _helpers;

    // Initializes slot tests against the shared disposable API database.
    public SlotAvailabilityTests(ApiFactory factory)
    {
        _factory = factory;
        _helpers = new TestHelpers(factory);
    }

    // Rule: the booking view returns only unbooked, future, seven-day slots in time order.
    [Fact]
    public async Task GetAvailableByStation_MixedSlots_ReturnsOnlySortedBookableSlots()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var later = await _helpers.CreateSlotAsync(admin, station.Id, 30);
        var sooner = await _helpers.CreateSlotAsync(admin, station.Id, 3);
        await _helpers.CreateSlotAsync(admin, station.Id, 200);
        var booked = await _helpers.CreateSlotAsync(admin, station.Id, 50);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var prosumerClient = prosumer.Client;
        await _helpers.BookAsync(prosumerClient, station.Id, booked.Id);
        var past = DateTime.UtcNow.AddHours(-2);
        await _factory.Database.GetCollection<EnergyBookingSlot>("EnergyBookingSlots").InsertOneAsync(new EnergyBookingSlot
        {
            StationId = station.Id,
            StationName = station.StationName,
            SlotDate = past.Date,
            StartTime = past,
            EndTime = past.AddHours(1),
            CapacityKw = 5,
            CreatedBy = "test",
        });

        var response = await prosumerClient.GetAsync($"/api/slots/station/{station.Id}/available");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var slots = await response.Content.ReadFromJsonAsync<List<SlotResponse>>();
        Assert.NotNull(slots);
        Assert.Equal(new[] { sooner.Id, later.Id }, slots.Select(slot => slot.Id));
        Assert.All(slots, slot => Assert.False(slot.IsBooked));
    }

    // Rule: availability for an inactive or unknown station is a bad request.
    [Fact]
    public async Task GetAvailableByStation_InactiveOrMissingStation_ReturnsBadRequest()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/api/stations/{station.Id}/deactivate", null)).StatusCode);
        var inactive = await admin.GetAsync($"/api/slots/station/{station.Id}/available");
        var missing = await admin.GetAsync("/api/slots/station/000000000000000000000000/available");
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    // Rule: a Prosumer cannot use any slot management endpoint.
    [Fact]
    public async Task ManageSlots_ProsumerRole_ReturnsForbiddenForEveryRoute()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var slot = await _helpers.CreateSlotAsync(admin, station.Id, 30);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var client = prosumer.Client;
        var start = DateTime.UtcNow.AddHours(50);
        var requests = new[]
        {
            await client.GetAsync("/api/slots"),
            await client.GetAsync($"/api/slots/{slot.Id}"),
            await client.GetAsync($"/api/slots/station/{station.Id}"),
            await client.PostAsJsonAsync("/api/slots", new { stationId = station.Id, slotDate = start.Date, startTime = start, endTime = start.AddHours(1), capacityKw = 5 }),
            await client.PostAsJsonAsync("/api/slots/bulk", new { stationId = station.Id, slotDate = start.Date, startTime = "09:00:00", endTime = "11:00:00", slotDurationMinutes = 60, capacityPerSlotKw = 5 }),
            await client.PutAsJsonAsync($"/api/slots/{slot.Id}", new { capacityKw = 4 }),
            await client.DeleteAsync($"/api/slots/{slot.Id}"),
        };
        Assert.All(requests, response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
    }
}
