/**
 * File: OperatorStationAssignmentTests.cs
 * Purpose: Integration checks for Backoffice-managed Grid Operator station assignments.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using System.Net;
using System.Net.Http.Json;
using MongoDB.Bson;
using SmartMicrogrid.API.Models;
using SmartMicrogrid.API.Tests.Infrastructure;
using Xunit;

namespace SmartMicrogrid.API.Tests;

[Collection("Api")]
public sealed class OperatorStationAssignmentTests
{
    private readonly ApiFactory _factory;
    private readonly TestHelpers _helpers;

    // Initializes station-assignment tests against the shared disposable API database.
    public OperatorStationAssignmentTests(ApiFactory factory)
    {
        _factory = factory;
        _helpers = new TestHelpers(factory);
    }

    // Rule: operator creation remains optional, persists valid stations, rejects invalid references, and leaves Backoffice unassigned.
    [Fact]
    public async Task CreateUser_OperatorStationAssignment_IsOptionalValidatedAndReturned()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);

        var withoutStationResponse = await admin.PostAsJsonAsync("/api/users", new
        {
            name = "Unassigned Operator",
            email = $"unassigned-{Guid.NewGuid():N}@example.com",
            password = "Operator@Test123",
            role = "GridOperator",
        });
        Assert.Equal(HttpStatusCode.Created, withoutStationResponse.StatusCode);
        var withoutStation = await withoutStationResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(withoutStation);
        Assert.Null(withoutStation.StationId);

        var withStationResponse = await admin.PostAsJsonAsync("/api/users", new
        {
            name = "Assigned Operator",
            email = $"assigned-{Guid.NewGuid():N}@example.com",
            password = "Operator@Test123",
            role = "GridOperator",
            stationId = station.Id,
        });
        Assert.Equal(HttpStatusCode.Created, withStationResponse.StatusCode);
        var withStation = await withStationResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(withStation);
        Assert.Equal(station.Id, withStation.StationId);

        var fetched = await admin.GetFromJsonAsync<UserResponse>($"/api/users/{withStation.Id}");
        Assert.Equal(station.Id, fetched!.StationId);

        var malformed = await admin.PostAsJsonAsync("/api/users", new
        {
            name = "Malformed Station Operator",
            email = $"malformed-{Guid.NewGuid():N}@example.com",
            password = "Operator@Test123",
            role = "GridOperator",
            stationId = "not-an-object-id",
        });
        var missing = await admin.PostAsJsonAsync("/api/users", new
        {
            name = "Missing Station Operator",
            email = $"missing-{Guid.NewGuid():N}@example.com",
            password = "Operator@Test123",
            role = "GridOperator",
            stationId = ObjectId.GenerateNewId().ToString(),
        });
        Assert.Equal(HttpStatusCode.Conflict, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);

        var backofficeResponse = await admin.PostAsJsonAsync("/api/users", new
        {
            name = "Additional Backoffice",
            email = $"backoffice-{Guid.NewGuid():N}@example.com",
            password = "Backoffice@Test123",
            role = "Backoffice",
            stationId = station.Id,
        });
        Assert.Equal(HttpStatusCode.Created, backofficeResponse.StatusCode);
        var backoffice = await backofficeResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(backoffice);
        Assert.Null(backoffice.StationId);
    }

    // Rule: operator updates preserve, replace, clear, and remove assignments according to the resulting role.
    [Fact]
    public async Task UpdateUser_OperatorStationAssignment_FollowsRoleAndPresenceSemantics()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var firstStation = await _helpers.CreateStationAsync(admin);
        var secondStation = await _helpers.CreateStationAsync(admin);
        var create = await admin.PostAsJsonAsync("/api/users", new
        {
            name = "Mutable Operator",
            email = $"mutable-{Guid.NewGuid():N}@example.com",
            password = "Operator@Test123",
            role = "GridOperator",
            stationId = firstStation.Id,
        });
        var user = await create.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(user);

        var preserved = await admin.PutAsJsonAsync($"/api/users/{user.Id}", new { name = "Renamed Operator" });
        Assert.Equal(firstStation.Id, (await preserved.Content.ReadFromJsonAsync<UserResponse>())!.StationId);

        var changed = await admin.PutAsJsonAsync($"/api/users/{user.Id}", new { stationId = secondStation.Id });
        Assert.Equal(secondStation.Id, (await changed.Content.ReadFromJsonAsync<UserResponse>())!.StationId);

        var malformed = await admin.PutAsJsonAsync($"/api/users/{user.Id}", new { stationId = "bad-id" });
        Assert.Equal(HttpStatusCode.Conflict, malformed.StatusCode);
        Assert.Equal(secondStation.Id, (await admin.GetFromJsonAsync<UserResponse>($"/api/users/{user.Id}"))!.StationId);

        var cleared = await admin.PutAsJsonAsync($"/api/users/{user.Id}", new { stationId = (string?)null });
        Assert.Null((await cleared.Content.ReadFromJsonAsync<UserResponse>())!.StationId);

        await admin.PutAsJsonAsync($"/api/users/{user.Id}", new { stationId = firstStation.Id });
        var changedToBackoffice = await admin.PutAsJsonAsync($"/api/users/{user.Id}", new { role = "Backoffice" });
        var backoffice = await changedToBackoffice.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("Backoffice", backoffice!.Role);
        Assert.Null(backoffice.StationId);

        var changedBackToOperator = await admin.PutAsJsonAsync(
            $"/api/users/{user.Id}",
            new { role = "GridOperator", stationId = secondStation.Id });
        var reassigned = await changedBackToOperator.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal("GridOperator", reassigned!.Role);
        Assert.Equal(secondStation.Id, reassigned.StationId);
    }

    // Rule: existing Backoffice list, get, update, password, deactivate, and reactivate behavior remains operational.
    [Fact]
    public async Task UserManagement_BackofficeLifecycle_RemainsOperational()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var originalEmail = $"managed-{Guid.NewGuid():N}@example.com";
        var updatedEmail = $"managed-updated-{Guid.NewGuid():N}@example.com";
        var create = await admin.PostAsJsonAsync("/api/users", new
        {
            name = "Managed Backoffice",
            email = originalEmail,
            password = "Backoffice@Test123",
            role = "Backoffice",
        });
        var created = await create.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(created);

        var listed = await admin.GetFromJsonAsync<List<UserResponse>>("/api/users?role=Backoffice&status=active");
        Assert.Contains(listed!, user => user.Id == created.Id);
        Assert.Equal(created.Id, (await admin.GetFromJsonAsync<UserResponse>($"/api/users/{created.Id}"))!.Id);

        var update = await admin.PutAsJsonAsync($"/api/users/{created.Id}", new
        {
            name = "Updated Backoffice",
            email = updatedEmail,
            password = "Updated@Test123",
        });
        var updated = await update.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Updated Backoffice", updated!.Name);
        Assert.Equal(updatedEmail, updated.Email);
        Assert.Null(updated.StationId);

        using var updatedLogin = await _helpers.LoginAsync(updatedEmail, "Updated@Test123");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/api/users/{created.Id}/deactivate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/api/users/{created.Id}/reactivate", null)).StatusCode);
        using var reactivatedLogin = await _helpers.LoginAsync(updatedEmail, "Updated@Test123");
    }

    // Rule: anonymous and GridOperator callers remain unable to use Backoffice-only user-management endpoints.
    [Fact]
    public async Task UserManagement_AnonymousAndGridOperator_AreRejected()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var anonymousClient = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync("/api/users")).StatusCode);

        var forbiddenCreate = await operatorClient.PostAsJsonAsync("/api/users", new
        {
            name = "Forbidden User",
            email = $"forbidden-{Guid.NewGuid():N}@example.com",
            password = "Operator@Test123",
            role = "GridOperator",
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenCreate.StatusCode);
    }
}
