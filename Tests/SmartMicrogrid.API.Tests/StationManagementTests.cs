/**
 * File: StationManagementTests.cs
 * Purpose: Integration checks for station persistence, lifecycle and mobile-role visibility.
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
public sealed class StationManagementTests
{
    private readonly TestHelpers _helpers;

    // Initializes station tests against the shared disposable API database.
    public StationManagementTests(ApiFactory factory)
    {
        _helpers = new TestHelpers(factory);
    }

    // Rule: creating a station persists its GPS, capacity and slot fields.
    [Fact]
    public async Task Create_ValidStation_PersistsAllFields()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var created = await _helpers.CreateStationAsync(admin, 7.2906, 80.6337, "Daily 09:00-17:00");
        var loaded = await admin.GetFromJsonAsync<StationResponse>($"/api/stations/{created.Id}");
        Assert.NotNull(loaded);
        Assert.Equal(7.2906, loaded.Latitude);
        Assert.Equal(80.6337, loaded.Longitude);
        Assert.Equal(85.5, loaded.CapacityKw);
        Assert.Equal(12, loaded.AvailableSlots);
        Assert.Equal("Daily 09:00-17:00", loaded.Schedule);
    }

    // Rule: station names are unique even when letter casing differs.
    [Fact]
    public async Task Create_DuplicateNameDifferentCase_ReturnsConflict()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var duplicate = await admin.PostAsJsonAsync("/api/stations", new
        {
            stationName = station.StationName.ToLowerInvariant(),
            latitude = station.Latitude,
            longitude = station.Longitude,
            capacityKw = 50,
            availableSlots = 1,
            schedule = "Daily",
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    // Rule: partial station updates leave unspecified fields unchanged.
    [Fact]
    public async Task Update_OnlyCapacitySupplied_PreservesOtherFields()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var response = await admin.PutAsJsonAsync($"/api/stations/{station.Id}", new { capacityKw = 99.5 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<StationResponse>();
        Assert.NotNull(updated);
        Assert.Equal(99.5, updated.CapacityKw);
        Assert.Equal(station.StationName, updated.StationName);
        Assert.Equal(station.Latitude, updated.Latitude);
        Assert.Equal(station.Longitude, updated.Longitude);
        Assert.Equal(station.AvailableSlots, updated.AvailableSlots);
        Assert.Equal(station.Schedule, updated.Schedule);
    }

    // Rule: an approved reservation blocks deactivation until completion, after which reactivation works.
    [Fact]
    public async Task Deactivate_ApprovedReservation_BlocksUntilCompleted()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var slot = await _helpers.CreateSlotAsync(admin, station.Id, 30);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var prosumerClient = prosumer.Client;
        var booking = await _helpers.BookAsync(prosumerClient, station.Id, slot.Id);
        await _helpers.ApproveAsync(admin, booking.Reservation.Id);

        var blocked = await admin.PutAsync($"/api/stations/{station.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        var completed = await admin.PutAsync($"/api/reservations/{booking.Reservation.Id}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var deactivated = await admin.PutAsync($"/api/stations/{station.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);
        Assert.False((await admin.GetFromJsonAsync<StationResponse>($"/api/stations/{station.Id}"))!.IsActive);
        var reactivated = await admin.PutAsync($"/api/stations/{station.Id}/reactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, reactivated.StatusCode);
        Assert.True((await admin.GetFromJsonAsync<StationResponse>($"/api/stations/{station.Id}"))!.IsActive);
    }

    // Rule: GridOperators and Prosumers cannot create, update or deactivate stations.
    [Fact]
    public async Task ManageStation_NonBackofficeRoles_ReturnForbidden()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin);
        var prosumerAccount = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var prosumerClient = prosumerAccount.Client;

        foreach (var client in new[] { operatorClient, prosumerClient })
        {
            var created = await client.PostAsJsonAsync("/api/stations", new
            {
                stationName = $"Forbidden {Guid.NewGuid():N}", latitude = 6.9, longitude = 79.8,
                capacityKw = 5, availableSlots = 1, schedule = "Daily",
            });
            var updated = await client.PutAsJsonAsync($"/api/stations/{station.Id}", new { capacityKw = 1 });
            var deactivated = await client.PutAsync($"/api/stations/{station.Id}/deactivate", null);
            Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, updated.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, deactivated.StatusCode);
        }
    }

    // Rule: non-Backoffice callers only discover active stations even with a crafted status query.
    [Fact]
    public async Task GetStations_NonBackofficeStatusOverride_HidesInactiveStations()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var active = await _helpers.CreateStationAsync(admin);
        var inactive = await _helpers.CreateStationAsync(admin);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/api/stations/{inactive.Id}/deactivate", null)).StatusCode);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin);
        var prosumerAccount = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var prosumerClient = prosumerAccount.Client;

        foreach (var client in new[] { operatorClient, prosumerClient })
        {
            var stations = await client.GetFromJsonAsync<List<StationResponse>>("/api/stations?status=deactivated");
            Assert.NotNull(stations);
            Assert.Contains(stations, station => station.Id == active.Id);
            Assert.DoesNotContain(stations, station => station.Id == inactive.Id);
            Assert.All(stations, station => Assert.True(station.IsActive));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/stations/{inactive.Id}")).StatusCode);
        }
    }
}
