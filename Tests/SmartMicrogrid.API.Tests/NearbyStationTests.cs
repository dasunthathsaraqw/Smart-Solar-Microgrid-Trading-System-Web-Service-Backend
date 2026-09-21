/**
 * File: NearbyStationTests.cs
 * Purpose: Integration checks for distance-ordered active stations and seven-day availability counts.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using System.Net;
using System.Net.Http.Json;
using MongoDB.Driver;
using SmartMicrogrid.API.Models;
using SmartMicrogrid.API.Tests.Infrastructure;
using Xunit;

namespace SmartMicrogrid.API.Tests;

[Collection("Api")]
public sealed class NearbyStationTests
{
    private readonly ApiFactory _factory;
    private readonly TestHelpers _helpers;

    // Initializes map-search tests against the shared isolated database.
    public NearbyStationTests(ApiFactory factory)
    {
        _factory = factory;
        _helpers = new TestHelpers(factory);
    }

    // Rule: nearby results are ordered from the shortest great-circle distance outward.
    [Fact]
    public async Task GetNearby_SeveralStations_SortsByAscendingDistance()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var far = await _helpers.CreateStationAsync(admin, 8.5200, 81.0000);
        var near = await _helpers.CreateStationAsync(admin, 8.5010, 81.0000);
        var results = await admin.GetFromJsonAsync<List<NearbyStationResponse>>(
            "/api/stations/nearby?latitude=8.5&longitude=81&radiusKm=4");
        Assert.NotNull(results);
        Assert.Contains(results, station => station.Id == far.Id);
        Assert.Contains(results, station => station.Id == near.Id);
        Assert.True(results.FindIndex(station => station.Id == near.Id) < results.FindIndex(station => station.Id == far.Id));
        Assert.Equal(results.Select(station => station.DistanceKm).OrderBy(distance => distance), results.Select(station => station.DistanceKm));
    }

    // Rule: stations outside the requested radius are excluded.
    [Fact]
    public async Task GetNearby_StationOutsideRadius_ExcludesIt()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var near = await _helpers.CreateStationAsync(admin, 8.1005, 81.1000);
        var far = await _helpers.CreateStationAsync(admin, 8.4000, 81.1000);
        var results = await admin.GetFromJsonAsync<List<NearbyStationResponse>>(
            "/api/stations/nearby?latitude=8.1&longitude=81.1&radiusKm=2");
        Assert.NotNull(results);
        Assert.Contains(results, station => station.Id == near.Id);
        Assert.DoesNotContain(results, station => station.Id == far.Id);
    }

    // Rule: invalid map coordinates or radius produce a client error instead of a search result.
    [Theory]
    [InlineData("latitude=91&longitude=80&radiusKm=10")]
    [InlineData("latitude=6&longitude=181&radiusKm=10")]
    [InlineData("latitude=6&longitude=80&radiusKm=0")]
    [InlineData("latitude=6&longitude=80&radiusKm=501")]
    public async Task GetNearby_InvalidCoordinateOrRadius_ReturnsBadRequest(string query)
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var response = await admin.GetAsync($"/api/stations/nearby?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Rule: availability counts only unbooked future slots within the next seven days.
    [Fact]
    public async Task GetNearby_MixedSlotStates_CountsOnlyBookableSlots()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin, 8.2000, 80.7000);
        await _helpers.CreateSlotAsync(admin, station.Id, 3);
        await _helpers.CreateSlotAsync(admin, station.Id, 30);
        await _helpers.CreateSlotAsync(admin, station.Id, 200);
        var bookedSlot = await _helpers.CreateSlotAsync(admin, station.Id, 48);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var prosumerClient = prosumer.Client;
        await _helpers.BookAsync(prosumerClient, station.Id, bookedSlot.Id);

        var past = DateTime.UtcNow.AddHours(-2);
        await _factory.Database.GetCollection<EnergyBookingSlot>("EnergyBookingSlots").InsertOneAsync(new EnergyBookingSlot
        {
            StationId = station.Id,
            StationName = station.StationName,
            SlotDate = past.Date,
            StartTime = past,
            EndTime = past.AddHours(1),
            CapacityKw = 5,
            IsBooked = false,
            CreatedBy = "test",
        });

        var results = await admin.GetFromJsonAsync<List<NearbyStationResponse>>(
            "/api/stations/nearby?latitude=8.2&longitude=80.7&radiusKm=1");
        var own = Assert.Single(results!, result => result.Id == station.Id);
        Assert.Equal(2, own.AvailableSlotCount);
    }

    // Rule: a deactivated station never appears in nearby search.
    [Fact]
    public async Task GetNearby_DeactivatedStation_ExcludesIt()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin, 8.3000, 81.3000);
        var deactivated = await admin.PutAsync($"/api/stations/{station.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);
        var results = await admin.GetFromJsonAsync<List<NearbyStationResponse>>(
            "/api/stations/nearby?latitude=8.3&longitude=81.3&radiusKm=1");
        Assert.DoesNotContain(results!, result => result.Id == station.Id);
    }
}
