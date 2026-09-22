/**
 * File: OperatorReservationScopeTests.cs
 * Purpose: Integration coverage for persisted GridOperator station scope across reservation
 *          listing, search, detail, lifecycle, QR retrieval and administrative role gates.
 * Author: Member 4
 * Date: 2026
 */

using System.Net;
using System.Net.Http.Json;
using SmartMicrogrid.API.Models;
using SmartMicrogrid.API.Tests.Infrastructure;
using Xunit;

namespace SmartMicrogrid.API.Tests;

[Collection("Api")]
public sealed class OperatorReservationScopeTests
{
    private readonly ApiFactory _factory;
    private readonly TestHelpers _helpers;

    // Initializes station-scope tests against the shared disposable API database.
    public OperatorReservationScopeTests(ApiFactory factory)
    {
        _factory = factory;
        _helpers = new TestHelpers(factory);
    }

    // Rule: operator list and search queries are database-scoped to the persisted station while preserving filters and paging.
    [Fact]
    public async Task ListAndSearch_AssignedOperator_AutoScopeWithoutCrossStationLeakage()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var foreignStation = await _helpers.CreateStationAsync(admin);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin, station.Id);
        var unassignedAccount = await _helpers.CreateGridOperatorAsync(admin);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var unassignedClient = unassignedAccount.Client;
        using var prosumerClient = prosumer.Client;

        var firstOwn = await CreatePendingReservationAsync(admin, prosumerClient, station.Id, 30);
        var secondOwn = await CreatePendingReservationAsync(admin, prosumerClient, station.Id, 40);
        var foreign = await CreatePendingReservationAsync(admin, prosumerClient, foreignStation.Id, 50);
        var nic = Uri.EscapeDataString(prosumer.Nic);

        var automatic = await operatorClient.GetFromJsonAsync<List<ReservationResponse>>(
            $"/api/reservations?prosumerNic={nic}");
        var explicitOwn = await operatorClient.GetFromJsonAsync<List<ReservationResponse>>(
            $"/api/reservations?stationId={station.Id}&prosumerNic={nic}");
        Assert.NotNull(automatic);
        Assert.NotNull(explicitOwn);
        Assert.Equal(2, automatic.Count);
        Assert.All(automatic, item => Assert.Equal(station.Id, item.StationId));
        Assert.DoesNotContain(automatic, item => item.Id == foreign.Id);
        Assert.Equal(
            automatic.Select(item => item.Id).Order(),
            explicitOwn.Select(item => item.Id).Order());

        await AssertForbiddenErrorAsync(
            await operatorClient.GetAsync($"/api/reservations?stationId={foreignStation.Id}"),
            "Grid Operator is not assigned to the requested station.");
        await AssertForbiddenErrorAsync(
            await unassignedClient.GetAsync("/api/reservations"),
            "Grid Operator is not assigned to a station.");

        var backoffice = await admin.GetFromJsonAsync<List<ReservationResponse>>(
            $"/api/reservations?prosumerNic={nic}");
        Assert.NotNull(backoffice);
        Assert.Contains(backoffice, item => item.Id == firstOwn.Id);
        Assert.Contains(backoffice, item => item.Id == secondOwn.Id);
        Assert.Contains(backoffice, item => item.Id == foreign.Id);

        var firstPageResponse = await operatorClient.PostAsJsonAsync("/api/reservations/search", new
        {
            prosumerNic = prosumer.Nic,
            status = "Pending",
            sortBy = "date",
            sortDir = "asc",
            page = 1,
            pageSize = 1,
        });
        var secondPageResponse = await operatorClient.PostAsJsonAsync("/api/reservations/search", new
        {
            prosumerNic = prosumer.Nic,
            status = "Pending",
            sortBy = "date",
            sortDir = "asc",
            page = 2,
            pageSize = 1,
        });
        var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<PagedResult<ReservationResponse>>();
        var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<PagedResult<ReservationResponse>>();
        Assert.Equal(HttpStatusCode.OK, firstPageResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondPageResponse.StatusCode);
        Assert.NotNull(firstPage);
        Assert.NotNull(secondPage);
        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(firstOwn.Id, Assert.Single(firstPage.Items).Id);
        Assert.Equal(secondOwn.Id, Assert.Single(secondPage.Items).Id);
        Assert.True(firstPage.HasNextPage);
        Assert.True(secondPage.HasPreviousPage);
        Assert.All(firstPage.Items.Concat(secondPage.Items), item => Assert.Equal(station.Id, item.StationId));

        var matchingSearch = await operatorClient.PostAsJsonAsync("/api/reservations/search", new
        {
            stationId = station.Id,
            prosumerNic = prosumer.Nic,
        });
        Assert.Equal(HttpStatusCode.OK, matchingSearch.StatusCode);

        var foreignSearch = await operatorClient.PostAsJsonAsync("/api/reservations/search", new
        {
            stationId = foreignStation.Id,
            prosumerNic = prosumer.Nic,
        });
        await AssertForbiddenErrorAsync(
            foreignSearch,
            "Grid Operator is not assigned to the requested station.");
    }

    // Rule: detail and on-behalf creation accept the assigned station, reject foreign scope, and retain service validation.
    [Fact]
    public async Task DetailAndCreate_EnforceScopeBeforeExistingBookingRules()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var foreignStation = await _helpers.CreateStationAsync(admin);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin, station.Id);
        var unassignedAccount = await _helpers.CreateGridOperatorAsync(admin);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var unassignedClient = unassignedAccount.Client;
        using var prosumerClient = prosumer.Client;

        var own = await CreatePendingReservationAsync(admin, prosumerClient, station.Id, 30);
        var foreign = await CreatePendingReservationAsync(admin, prosumerClient, foreignStation.Id, 40);

        var ownDetail = await operatorClient.GetFromJsonAsync<ReservationResponse>(
            $"/api/reservations/{own.Id}");
        Assert.NotNull(ownDetail);
        Assert.Equal(station.Id, ownDetail.StationId);
        await AssertForbiddenErrorAsync(
            await operatorClient.GetAsync($"/api/reservations/{foreign.Id}"),
            "Grid Operator is not assigned to this reservation's station.");

        var ownSlot = await _helpers.CreateSlotAsync(admin, station.Id, 50);
        var ownCreate = await operatorClient.PostAsJsonAsync("/api/reservations", new
        {
            prosumerNic = prosumer.Nic,
            stationId = station.Id,
            slotId = ownSlot.Id,
        });
        Assert.Equal(HttpStatusCode.Created, ownCreate.StatusCode);
        Assert.Equal(
            station.Id,
            (await ownCreate.Content.ReadFromJsonAsync<ReservationResponse>())!.StationId);

        var foreignSlot = await _helpers.CreateSlotAsync(admin, foreignStation.Id, 60);
        await AssertForbiddenErrorAsync(
            await operatorClient.PostAsJsonAsync("/api/reservations", new
            {
                prosumerNic = prosumer.Nic,
                stationId = foreignStation.Id,
                slotId = foreignSlot.Id,
            }),
            "Grid Operator is not assigned to the requested station.");
        await AssertForbiddenErrorAsync(
            await unassignedClient.PostAsJsonAsync("/api/reservations", new
            {
                prosumerNic = prosumer.Nic,
                stationId = station.Id,
                slotId = ownSlot.Id,
            }),
            "Grid Operator is not assigned to a station.");

        var mismatchedSlot = await _helpers.CreateSlotAsync(admin, foreignStation.Id, 70);
        var existingValidation = await operatorClient.PostAsJsonAsync("/api/reservations", new
        {
            prosumerNic = prosumer.Nic,
            stationId = station.Id,
            slotId = mismatchedSlot.Id,
        });
        Assert.Equal(HttpStatusCode.BadRequest, existingValidation.StatusCode);
        var validationError = await existingValidation.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("Slot does not belong to this station", validationError!["error"]);

        var backofficeSlot = await _helpers.CreateSlotAsync(admin, foreignStation.Id, 80);
        var backofficeCreate = await admin.PostAsJsonAsync("/api/reservations", new
        {
            prosumerNic = prosumer.Nic,
            stationId = foreignStation.Id,
            slotId = backofficeSlot.Id,
        });
        Assert.Equal(HttpStatusCode.Created, backofficeCreate.StatusCode);
    }

    // Rule: rescheduling validates both the existing reservation station and the destination slot station.
    [Fact]
    public async Task Update_RequiresOwnReservationAndOwnDestinationSlot()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var foreignStation = await _helpers.CreateStationAsync(admin);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin, station.Id);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var prosumerClient = prosumer.Client;

        var own = await CreatePendingReservationAsync(admin, prosumerClient, station.Id, 30);
        var ownDestination = await _helpers.CreateSlotAsync(admin, station.Id, 40);
        var foreignDestination = await _helpers.CreateSlotAsync(admin, foreignStation.Id, 50);
        var foreign = await CreatePendingReservationAsync(admin, prosumerClient, foreignStation.Id, 60);
        var secondForeignDestination = await _helpers.CreateSlotAsync(admin, foreignStation.Id, 70);

        var ownUpdate = await operatorClient.PutAsJsonAsync(
            $"/api/reservations/{own.Id}",
            new { newSlotId = ownDestination.Id });
        Assert.Equal(HttpStatusCode.OK, ownUpdate.StatusCode);
        Assert.Equal(
            ownDestination.Id,
            (await ownUpdate.Content.ReadFromJsonAsync<ReservationResponse>())!.SlotId);

        await AssertForbiddenErrorAsync(
            await operatorClient.PutAsJsonAsync(
                $"/api/reservations/{own.Id}",
                new { newSlotId = foreignDestination.Id }),
            "Grid Operator is not assigned to the requested station.");
        await AssertForbiddenErrorAsync(
            await operatorClient.PutAsJsonAsync(
                $"/api/reservations/{foreign.Id}",
                new { newSlotId = secondForeignDestination.Id }),
            "Grid Operator is not assigned to this reservation's station.");

        var backofficeUpdate = await admin.PutAsJsonAsync(
            $"/api/reservations/{foreign.Id}",
            new { newSlotId = secondForeignDestination.Id });
        Assert.Equal(HttpStatusCode.OK, backofficeUpdate.StatusCode);
        Assert.Equal(
            secondForeignDestination.Id,
            (await backofficeUpdate.Content.ReadFromJsonAsync<ReservationResponse>())!.SlotId);
    }

    // Rule: cancel, approve, direct complete and QR retrieval operate only on reservations at the assigned station.
    [Fact]
    public async Task LifecycleAndQr_EnforceAssignedStationWithoutChangingTransitions()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var foreignStation = await _helpers.CreateStationAsync(admin);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin, station.Id);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var prosumerClient = prosumer.Client;

        var ownCancellation = await CreatePendingReservationAsync(admin, prosumerClient, station.Id, 30);
        var foreignCancellation = await CreatePendingReservationAsync(admin, prosumerClient, foreignStation.Id, 40);
        var ownApproval = await CreatePendingReservationAsync(admin, prosumerClient, station.Id, 50);
        var foreignApproval = await CreatePendingReservationAsync(admin, prosumerClient, foreignStation.Id, 60);
        var ownCompletion = await CreatePendingReservationAsync(admin, prosumerClient, station.Id, 70);
        var foreignCompletion = await CreatePendingReservationAsync(admin, prosumerClient, foreignStation.Id, 80);

        var cancelled = await operatorClient.PutAsJsonAsync(
            $"/api/reservations/{ownCancellation.Id}/cancel",
            new { reason = "Operator cancellation" });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(
            "Cancelled",
            (await cancelled.Content.ReadFromJsonAsync<ReservationResponse>())!.Status);
        await AssertForbiddenErrorAsync(
            await operatorClient.PutAsJsonAsync(
                $"/api/reservations/{foreignCancellation.Id}/cancel",
                new { reason = "Must be rejected" }),
            "Grid Operator is not assigned to this reservation's station.");
        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PutAsJsonAsync(
                $"/api/reservations/{foreignCancellation.Id}/cancel",
                new { reason = "Backoffice remains unrestricted" })).StatusCode);

        var approved = await operatorClient.PutAsync($"/api/reservations/{ownApproval.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var approvedReservation = await approved.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.NotNull(approvedReservation);
        Assert.Equal("Approved", approvedReservation.Status);
        Assert.NotNull(approvedReservation.QrGeneratedAt);
        await AssertForbiddenErrorAsync(
            await operatorClient.PutAsync($"/api/reservations/{foreignApproval.Id}/approve", null),
            "Grid Operator is not assigned to this reservation's station.");
        await _helpers.ApproveAsync(admin, foreignApproval.Id);

        var ownQr = await operatorClient.GetAsync($"/api/reservations/{ownApproval.Id}/qr");
        Assert.Equal(HttpStatusCode.OK, ownQr.StatusCode);
        var qrPayload = await ownQr.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.False(string.IsNullOrWhiteSpace(qrPayload!["qrToken"]));
        await AssertForbiddenErrorAsync(
            await operatorClient.GetAsync($"/api/reservations/{foreignApproval.Id}/qr"),
            "Grid Operator is not assigned to this reservation's station.");
        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.GetAsync($"/api/reservations/{foreignApproval.Id}/qr")).StatusCode);

        await _helpers.ApproveAsync(operatorClient, ownCompletion.Id);
        var completed = await operatorClient.PutAsync($"/api/reservations/{ownCompletion.Id}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var completedReservation = await completed.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.NotNull(completedReservation);
        Assert.Equal("Completed", completedReservation.Status);
        Assert.Equal(operatorAccount.Email, completedReservation.CompletedBy);

        await _helpers.ApproveAsync(admin, foreignCompletion.Id);
        await AssertForbiddenErrorAsync(
            await operatorClient.PutAsync($"/api/reservations/{foreignCompletion.Id}/complete", null),
            "Grid Operator is not assigned to this reservation's station.");
        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PutAsync($"/api/reservations/{foreignCompletion.Id}/complete", null)).StatusCode);
    }

    // Rule: every administrative reservation route rejects unassigned operators, Prosumers and anonymous callers consistently.
    [Fact]
    public async Task AdministrativeRoutes_EnforceAssignmentAndRoleGates()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var unassignedAccount = await _helpers.CreateGridOperatorAsync(admin);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var unassignedClient = unassignedAccount.Client;
        using var prosumerClient = prosumer.Client;
        using var anonymousClient = _factory.CreateClient();
        var reservation = await CreatePendingReservationAsync(admin, prosumerClient, station.Id, 30);
        var destination = await _helpers.CreateSlotAsync(admin, station.Id, 40);

        var requests = new Func<HttpClient, Task<HttpResponseMessage>>[]
        {
            client => client.GetAsync("/api/reservations"),
            client => client.PostAsJsonAsync("/api/reservations/search", new { stationId = station.Id }),
            client => client.GetAsync($"/api/reservations/{reservation.Id}"),
            client => client.PostAsJsonAsync("/api/reservations", new
            {
                prosumerNic = prosumer.Nic,
                stationId = station.Id,
                slotId = destination.Id,
            }),
            client => client.PutAsJsonAsync($"/api/reservations/{reservation.Id}", new { newSlotId = destination.Id }),
            client => client.PutAsJsonAsync($"/api/reservations/{reservation.Id}/cancel", new { reason = "Denied" }),
            client => client.PutAsync($"/api/reservations/{reservation.Id}/approve", null),
            client => client.PutAsync($"/api/reservations/{reservation.Id}/complete", null),
            client => client.GetAsync($"/api/reservations/{reservation.Id}/qr"),
        };

        foreach (var request in requests)
        {
            await AssertForbiddenErrorAsync(
                await request(unassignedClient),
                "Grid Operator is not assigned to a station.");
            Assert.Equal(HttpStatusCode.Forbidden, (await request(prosumerClient)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await request(anonymousClient)).StatusCode);
        }
    }

    // Creates a Pending reservation through the existing Prosumer workflow for a scoped scenario.
    private async Task<ReservationResponse> CreatePendingReservationAsync(
        HttpClient admin,
        HttpClient prosumer,
        string stationId,
        double hoursAhead)
    {
        var slot = await _helpers.CreateSlotAsync(admin, stationId, hoursAhead);
        return (await _helpers.BookAsync(prosumer, stationId, slot.Id)).Reservation;
    }

    // Asserts the established 403 JSON contract without depending on any reservation details.
    private static async Task AssertForbiddenErrorAsync(HttpResponseMessage response, string expectedError)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body);
        Assert.Equal(expectedError, body["error"]);
    }
}
