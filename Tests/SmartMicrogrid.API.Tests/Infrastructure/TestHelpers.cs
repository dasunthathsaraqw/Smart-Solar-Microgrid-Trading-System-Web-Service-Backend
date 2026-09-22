/**
 * File: TestHelpers.cs
 * Purpose: Assertive API setup helpers for authenticated integration scenarios.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SmartMicrogrid.API.Models;
using Xunit;

namespace SmartMicrogrid.API.Tests.Infrastructure;

public sealed record ProsumerAccount(HttpClient Client, string Nic, string Email, string Password);
public sealed record OperatorAccount(HttpClient Client, string Email, string Password);

public sealed class TestHelpers
{
    private readonly ApiFactory _factory;
    private static int _nicSequence = 12345;

    // Binds setup helpers to the shared in-memory API host.
    public TestHelpers(ApiFactory factory)
    {
        _factory = factory;
    }

    // Logs in and returns a bearer-authorized client, asserting credentials are accepted.
    public async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Login for {email} failed: {await response.Content.ReadAsStringAsync()}");
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        Assert.False(string.IsNullOrWhiteSpace(login.Token));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        return client;
    }

    // Creates a uniquely named station and asserts its POST response contains a persisted ID.
    public async Task<StationResponse> CreateStationAsync(HttpClient admin, double latitude = 6.9271, double longitude = 79.8612, string schedule = "Daily 00:00-23:59")
    {
        var response = await admin.PostAsJsonAsync("/api/stations", new
        {
            stationName = $"Test Station {Guid.NewGuid():N}",
            latitude,
            longitude,
            capacityKw = 85.5,
            availableSlots = 12,
            schedule,
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Station setup failed: {await response.Content.ReadAsStringAsync()}");
        var station = await response.Content.ReadFromJsonAsync<StationResponse>();
        Assert.NotNull(station);
        Assert.False(string.IsNullOrWhiteSpace(station.Id));
        return station;
    }

    // Creates a one-hour future slot at the requested offset and asserts successful persistence.
    public async Task<SlotResponse> CreateSlotAsync(HttpClient manager, string stationId, double hoursAhead)
    {
        var start = DateTime.UtcNow.AddHours(hoursAhead);
        var response = await manager.PostAsJsonAsync("/api/slots", new
        {
            stationId,
            slotDate = start.Date,
            startTime = start,
            endTime = start.AddHours(1),
            capacityKw = 5.0,
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Slot setup failed: {await response.Content.ReadAsStringAsync()}");
        var slot = await response.Content.ReadFromJsonAsync<SlotResponse>();
        Assert.NotNull(slot);
        Assert.False(string.IsNullOrWhiteSpace(slot.Id));
        return slot;
    }

    // Self-registers a distinct pending prosumer, approves it, then asserts active login works.
    public async Task<ProsumerAccount> RegisterAndApproveProsumerAsync(HttpClient admin)
    {
        var nic = $"2000{Interlocked.Increment(ref _nicSequence):D8}";
        var email = $"prosumer-{Guid.NewGuid():N}@example.com";
        const string password = "Prosumer@Test123";
        var registration = await _factory.CreateClient().PostAsJsonAsync("/api/prosumers/register", new
        {
            nic,
            name = "Integration Prosumer",
            email,
            password,
            contactNumber = "0771234567",
            address = "Colombo, Sri Lanka",
            panelCapacityKw = 8.5,
        });
        Assert.True(registration.StatusCode == HttpStatusCode.Created, $"Registration setup failed: {await registration.Content.ReadAsStringAsync()}");
        var approval = await admin.PutAsync($"/api/prosumers/{nic}/reactivate", null);
        Assert.True(approval.StatusCode == HttpStatusCode.NoContent, $"Approval setup failed: {await approval.Content.ReadAsStringAsync()}");
        return new ProsumerAccount(await LoginAsync(email, password), nic, email, password);
    }

    // Creates and logs in a distinct GridOperator account, optionally assigned to a station.
    public async Task<OperatorAccount> CreateGridOperatorAsync(HttpClient admin, string? stationId = null)
    {
        var email = $"operator-{Guid.NewGuid():N}@example.com";
        const string password = "Operator@Test123";
        var response = await admin.PostAsJsonAsync("/api/users", new
        {
            name = "Integration Operator",
            email,
            password,
            role = "GridOperator",
            stationId,
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Operator setup failed: {await response.Content.ReadAsStringAsync()}");
        return new OperatorAccount(await LoginAsync(email, password), email, password);
    }

    // Books an owned slot and asserts the server returns the mobile confirmation envelope.
    public async Task<ReservationActionResponse> BookAsync(HttpClient prosumer, string stationId, string slotId, string? claimedNic = null)
    {
        var response = await prosumer.PostAsJsonAsync("/api/reservations/my", new { stationId, slotId, prosumerNic = claimedNic });
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Booking setup failed: {await response.Content.ReadAsStringAsync()}");
        var action = await response.Content.ReadFromJsonAsync<ReservationActionResponse>();
        Assert.NotNull(action);
        Assert.False(string.IsNullOrWhiteSpace(action.Reservation.Id));
        return action;
    }

    // Approves a pending reservation and asserts the server accepts the transition.
    public async Task ApproveAsync(HttpClient manager, string reservationId)
    {
        var response = await manager.PutAsync($"/api/reservations/{reservationId}/approve", null);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Approval setup failed: {await response.Content.ReadAsStringAsync()}");
    }
}
