/**
 * File: OperatorSlotScopeTests.cs
 * Purpose: Integration coverage for persisted GridOperator station scope across slot reads and writes.
 * Author: Member 4
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
public sealed class OperatorSlotScopeTests
{
    private readonly ApiFactory _factory;
    private readonly TestHelpers _helpers;

    // Initializes station-scope tests against the shared disposable API database.
    public OperatorSlotScopeTests(ApiFactory factory)
    {
        _factory = factory;
        _helpers = new TestHelpers(factory);
    }

    // Rule: GridOperator lists are database-scoped to the persisted station even when stationId is omitted.
    [Fact]
    public async Task List_AssignedOperator_AutoScopesAndRejectsForeignOrMissingAssignment()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var foreignStation = await _helpers.CreateStationAsync(admin);
        var ownFirst = await _helpers.CreateSlotAsync(admin, station.Id, 30);
        var ownSecond = await _helpers.CreateSlotAsync(admin, station.Id, 40);
        var foreign = await _helpers.CreateSlotAsync(admin, foreignStation.Id, 50);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin, station.Id);
        var unassignedAccount = await _helpers.CreateGridOperatorAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var unassignedClient = unassignedAccount.Client;

        var automatic = await operatorClient.GetFromJsonAsync<List<SlotResponse>>("/api/slots");
        var explicitOwn = await operatorClient.GetFromJsonAsync<List<SlotResponse>>(
            $"/api/slots?stationId={station.Id}");
        var availableOwn = await operatorClient.GetFromJsonAsync<List<SlotResponse>>(
            "/api/slots?status=available");

        Assert.NotNull(automatic);
        Assert.NotNull(explicitOwn);
        Assert.NotNull(availableOwn);
        Assert.Equal(new[] { ownFirst.Id, ownSecond.Id }, automatic.Select(slot => slot.Id));
        Assert.Equal(automatic.Select(slot => slot.Id), explicitOwn.Select(slot => slot.Id));
        Assert.Equal(automatic.Select(slot => slot.Id), availableOwn.Select(slot => slot.Id));
        Assert.All(automatic, slot => Assert.Equal(station.Id, slot.StationId));
        Assert.DoesNotContain(automatic, slot => slot.Id == foreign.Id);

        await AssertForbiddenErrorAsync(
            await operatorClient.GetAsync($"/api/slots?stationId={foreignStation.Id}"),
            "Grid Operator is not assigned to the requested station.");
        await AssertForbiddenErrorAsync(
            await unassignedClient.GetAsync("/api/slots"),
            "Grid Operator is not assigned to a station.");

        var backoffice = await admin.GetFromJsonAsync<List<SlotResponse>>("/api/slots");
        Assert.NotNull(backoffice);
        Assert.Contains(backoffice, slot => slot.Id == ownFirst.Id);
        Assert.Contains(backoffice, slot => slot.Id == foreign.Id);
    }

    // Rule: station routes scope only GridOperators and preserve Backoffice and Prosumer availability behavior.
    [Fact]
    public async Task StationRoutes_EnforceOperatorScopeWithoutRestrictingProsumerAvailability()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var foreignStation = await _helpers.CreateStationAsync(admin);
        var own = await _helpers.CreateSlotAsync(admin, station.Id, 30);
        var foreign = await _helpers.CreateSlotAsync(admin, foreignStation.Id, 40);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin, station.Id);
        var unassignedAccount = await _helpers.CreateGridOperatorAsync(admin);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var unassignedClient = unassignedAccount.Client;
        using var prosumerClient = prosumer.Client;
        using var anonymousClient = _factory.CreateClient();

        var ownStationSlots = await operatorClient.GetFromJsonAsync<List<SlotResponse>>(
            $"/api/slots/station/{station.Id}");
        var ownAvailable = await operatorClient.GetFromJsonAsync<List<SlotResponse>>(
            $"/api/slots/station/{station.Id}/available");
        Assert.Contains(ownStationSlots!, slot => slot.Id == own.Id);
        Assert.Contains(ownAvailable!, slot => slot.Id == own.Id);

        await AssertForbiddenErrorAsync(
            await operatorClient.GetAsync($"/api/slots/station/{foreignStation.Id}"),
            "Grid Operator is not assigned to the requested station.");
        await AssertForbiddenErrorAsync(
            await operatorClient.GetAsync($"/api/slots/station/{foreignStation.Id}/available"),
            "Grid Operator is not assigned to the requested station.");
        await AssertForbiddenErrorAsync(
            await unassignedClient.GetAsync($"/api/slots/station/{station.Id}"),
            "Grid Operator is not assigned to a station.");
        await AssertForbiddenErrorAsync(
            await unassignedClient.GetAsync($"/api/slots/station/{station.Id}/available"),
            "Grid Operator is not assigned to a station.");

        var prosumerAvailable = await prosumerClient.GetFromJsonAsync<List<SlotResponse>>(
            $"/api/slots/station/{foreignStation.Id}/available");
        Assert.Contains(prosumerAvailable!, slot => slot.Id == foreign.Id);
        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.GetAsync($"/api/slots/station/{foreignStation.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymousClient.GetAsync($"/api/slots/station/{station.Id}/available")).StatusCode);
    }

    // Rule: slot detail checks the stored station without disclosing foreign slot data.
    [Fact]
    public async Task GetById_AllowsOwnSlotAndRejectsForeignSlot()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var foreignStation = await _helpers.CreateStationAsync(admin);
        var own = await _helpers.CreateSlotAsync(admin, station.Id, 30);
        var foreign = await _helpers.CreateSlotAsync(admin, foreignStation.Id, 40);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin, station.Id);
        var unassignedAccount = await _helpers.CreateGridOperatorAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var unassignedClient = unassignedAccount.Client;

        var ownDetail = await operatorClient.GetFromJsonAsync<SlotResponse>($"/api/slots/{own.Id}");
        Assert.NotNull(ownDetail);
        Assert.Equal(station.Id, ownDetail.StationId);

        await AssertForbiddenErrorAsync(
            await operatorClient.GetAsync($"/api/slots/{foreign.Id}"),
            "Grid Operator is not assigned to the requested station.");
        await AssertForbiddenErrorAsync(
            await unassignedClient.GetAsync($"/api/slots/{own.Id}"),
            "Grid Operator is not assigned to a station.");
        Assert.Equal(HttpStatusCode.NotFound, (await operatorClient.GetAsync("/api/slots/000000000000000000000000")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/slots/{foreign.Id}")).StatusCode);
    }

    // Rule: create and bulk-create authorize the single request station before any slot is persisted.
    [Fact]
    public async Task CreateAndBulkCreate_RejectForeignWritesAtomicallyAndPreserveBackofficeAccess()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var foreignStation = await _helpers.CreateStationAsync(admin);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin, station.Id);
        var unassignedAccount = await _helpers.CreateGridOperatorAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var unassignedClient = unassignedAccount.Client;
        var slots = _factory.Database.GetCollection<EnergyBookingSlot>("EnergyBookingSlots");

        var ownStart = DateTime.UtcNow.AddHours(100);
        var ownCreate = await operatorClient.PostAsJsonAsync("/api/slots", SlotPayload(station.Id, ownStart));
        Assert.Equal(HttpStatusCode.Created, ownCreate.StatusCode);

        var foreignCount = await slots.CountDocumentsAsync(slot => slot.StationId == foreignStation.Id);
        var foreignStart = DateTime.UtcNow.AddHours(110);
        await AssertForbiddenErrorAsync(
            await operatorClient.PostAsJsonAsync("/api/slots", SlotPayload(foreignStation.Id, foreignStart)),
            "Grid Operator is not assigned to the requested station.");
        Assert.Equal(foreignCount, await slots.CountDocumentsAsync(slot => slot.StationId == foreignStation.Id));

        await AssertForbiddenErrorAsync(
            await unassignedClient.PostAsJsonAsync(
                "/api/slots",
                SlotPayload(station.Id, DateTime.UtcNow.AddHours(120))),
            "Grid Operator is not assigned to a station.");

        var ownBulk = await operatorClient.PostAsJsonAsync(
            "/api/slots/bulk",
            BulkPayload(station.Id, DateTime.UtcNow.Date.AddDays(12)));
        Assert.Equal(HttpStatusCode.Created, ownBulk.StatusCode);
        Assert.NotEmpty((await ownBulk.Content.ReadFromJsonAsync<List<SlotResponse>>())!);

        foreignCount = await slots.CountDocumentsAsync(slot => slot.StationId == foreignStation.Id);
        await AssertForbiddenErrorAsync(
            await operatorClient.PostAsJsonAsync(
                "/api/slots/bulk",
                BulkPayload(foreignStation.Id, DateTime.UtcNow.Date.AddDays(13))),
            "Grid Operator is not assigned to the requested station.");
        Assert.Equal(foreignCount, await slots.CountDocumentsAsync(slot => slot.StationId == foreignStation.Id));

        await AssertForbiddenErrorAsync(
            await unassignedClient.PostAsJsonAsync(
                "/api/slots/bulk",
                BulkPayload(station.Id, DateTime.UtcNow.Date.AddDays(13))),
            "Grid Operator is not assigned to a station.");

        Assert.Equal(
            HttpStatusCode.Created,
            (await admin.PostAsJsonAsync(
                "/api/slots",
                SlotPayload(foreignStation.Id, DateTime.UtcNow.AddHours(140)))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            (await admin.PostAsJsonAsync(
                "/api/slots/bulk",
                BulkPayload(foreignStation.Id, DateTime.UtcNow.Date.AddDays(14)))).StatusCode);
    }

    // Rule: updates authorize the stored station first and retain booked-slot business validation.
    [Fact]
    public async Task Update_EnforcesStoredStationAndPreservesBookedSlotProtection()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var foreignStation = await _helpers.CreateStationAsync(admin);
        var own = await _helpers.CreateSlotAsync(admin, station.Id, 100);
        var foreign = await _helpers.CreateSlotAsync(admin, foreignStation.Id, 110);
        var booked = await _helpers.CreateSlotAsync(admin, station.Id, 40);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin, station.Id);
        var unassignedAccount = await _helpers.CreateGridOperatorAsync(admin);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var unassignedClient = unassignedAccount.Client;
        using var prosumerClient = prosumer.Client;
        await _helpers.BookAsync(prosumerClient, station.Id, booked.Id);

        var ownUpdate = await operatorClient.PutAsJsonAsync($"/api/slots/{own.Id}", new { capacityKw = 7.0 });
        Assert.Equal(HttpStatusCode.OK, ownUpdate.StatusCode);
        Assert.Equal(7.0, (await ownUpdate.Content.ReadFromJsonAsync<SlotResponse>())!.CapacityKw);

        await AssertForbiddenErrorAsync(
            await operatorClient.PutAsJsonAsync($"/api/slots/{foreign.Id}", new { capacityKw = 8.0 }),
            "Grid Operator is not assigned to the requested station.");
        Assert.Equal(5.0, (await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{foreign.Id}"))!.CapacityKw);

        await AssertForbiddenErrorAsync(
            await unassignedClient.PutAsJsonAsync($"/api/slots/{own.Id}", new { capacityKw = 9.0 }),
            "Grid Operator is not assigned to a station.");
        Assert.Equal(7.0, (await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{own.Id}"))!.CapacityKw);

        var bookedUpdate = await operatorClient.PutAsJsonAsync($"/api/slots/{booked.Id}", new { capacityKw = 6.0 });
        Assert.Equal(HttpStatusCode.BadRequest, bookedUpdate.StatusCode);
        Assert.Equal("Cannot modify a booked slot", (await ReadErrorAsync(bookedUpdate)));
        var bookedState = await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{booked.Id}");
        Assert.True(bookedState!.IsBooked);
        Assert.Equal(5.0, bookedState.CapacityKw);

        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PutAsJsonAsync($"/api/slots/{foreign.Id}", new { capacityKw = 8.0 })).StatusCode);
    }

    // Rule: deletes authorize the stored station first and retain booked-slot business validation.
    [Fact]
    public async Task Delete_EnforcesStoredStationAndPreservesBookedSlotProtection()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var foreignStation = await _helpers.CreateStationAsync(admin);
        var own = await _helpers.CreateSlotAsync(admin, station.Id, 100);
        var unassignedTarget = await _helpers.CreateSlotAsync(admin, station.Id, 110);
        var foreign = await _helpers.CreateSlotAsync(admin, foreignStation.Id, 120);
        var backofficeTarget = await _helpers.CreateSlotAsync(admin, foreignStation.Id, 130);
        var booked = await _helpers.CreateSlotAsync(admin, station.Id, 40);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin, station.Id);
        var unassignedAccount = await _helpers.CreateGridOperatorAsync(admin);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var unassignedClient = unassignedAccount.Client;
        using var prosumerClient = prosumer.Client;
        await _helpers.BookAsync(prosumerClient, station.Id, booked.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await operatorClient.DeleteAsync($"/api/slots/{own.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/slots/{own.Id}")).StatusCode);

        await AssertForbiddenErrorAsync(
            await operatorClient.DeleteAsync($"/api/slots/{foreign.Id}"),
            "Grid Operator is not assigned to the requested station.");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/slots/{foreign.Id}")).StatusCode);

        await AssertForbiddenErrorAsync(
            await unassignedClient.DeleteAsync($"/api/slots/{unassignedTarget.Id}"),
            "Grid Operator is not assigned to a station.");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/slots/{unassignedTarget.Id}")).StatusCode);

        var bookedDelete = await operatorClient.DeleteAsync($"/api/slots/{booked.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, bookedDelete.StatusCode);
        Assert.Equal("Cannot modify a booked slot", await ReadErrorAsync(bookedDelete));
        var bookedState = await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{booked.Id}");
        Assert.True(bookedState!.IsBooked);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/slots/{backofficeTarget.Id}")).StatusCode);
    }

    // Produces a valid single-slot request with the route's existing timing and capacity rules.
    private static object SlotPayload(string stationId, DateTime startTime) => new
    {
        stationId,
        slotDate = startTime.Date,
        startTime,
        endTime = startTime.AddHours(1),
        capacityKw = 5.0,
    };

    // Produces a valid two-slot batch. The existing request model has one station for the entire batch.
    private static object BulkPayload(string stationId, DateTime slotDate) => new
    {
        stationId,
        slotDate,
        startTime = "09:00:00",
        endTime = "11:00:00",
        slotDurationMinutes = 60,
        capacityPerSlotKw = 5.0,
    };

    // Reads the established JSON error envelope.
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body);
        return body["error"];
    }

    // Asserts the established station-scope 403 contract.
    private static async Task AssertForbiddenErrorAsync(HttpResponseMessage response, string expectedError)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(expectedError, await ReadErrorAsync(response));
    }
}
