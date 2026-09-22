/**
 * File: OperatorEndToEndTests.cs
 * Purpose: End-to-end integration coverage for the complete GridOperator transaction workflow,
 *          station isolation, assignment refresh, role compatibility and published API contract.
 * Author: Member 4
 * Date: 2026
 */

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using SmartMicrogrid.API.Models;
using SmartMicrogrid.API.Tests.Infrastructure;
using Swashbuckle.AspNetCore.Swagger;
using Xunit;

namespace SmartMicrogrid.API.Tests;

[Collection("Api")]
public sealed class OperatorEndToEndTests
{
    private readonly ApiFactory _factory;
    private readonly TestHelpers _helpers;

    // Initializes end-to-end scenarios against the shared disposable API database.
    public OperatorEndToEndTests(ApiFactory factory)
    {
        _factory = factory;
        _helpers = new TestHelpers(factory);
    }

    // Proves the complete Pending-to-Approved-to-QR-scan-to-Completed operator transaction lifecycle.
    [Fact]
    public async Task CompleteOperatorWorkflow_UsesQrAndUpdatesDashboardHistoryAndPersistedState()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var unrelatedStation = await _helpers.CreateStationAsync(admin);
        var slot = await _helpers.CreateSlotAsync(admin, station.Id, 2);
        var unrelatedSlot = await _helpers.CreateSlotAsync(admin, unrelatedStation.Id, 3);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin, station.Id);
        using var initialOperatorClient = operatorAccount.Client;
        using var prosumerClient = prosumer.Client;

        var booking = await _helpers.BookAsync(prosumerClient, station.Id, slot.Id);
        Assert.Equal("Pending", booking.Reservation.Status);
        var pending = await admin.GetFromJsonAsync<ReservationResponse>(
            $"/api/reservations/{booking.Reservation.Id}");
        Assert.NotNull(pending);
        Assert.Equal("Pending", pending.Status);
        Assert.True((await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{slot.Id}"))!.IsBooked);

        var unrelatedBooking = await _helpers.BookAsync(
            prosumerClient,
            unrelatedStation.Id,
            unrelatedSlot.Id);
        await _helpers.ApproveAsync(admin, unrelatedBooking.Reservation.Id);

        var approval = await admin.PutAsync($"/api/reservations/{booking.Reservation.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        var approved = await approval.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.NotNull(approved);
        Assert.Equal("Approved", approved.Status);
        Assert.NotNull(approved.QrGeneratedAt);
        var firstQr = await prosumerClient.GetFromJsonAsync<Dictionary<string, string>>(
            $"/api/reservations/my/{booking.Reservation.Id}/qr");
        var qrToken = firstQr!["qrToken"];
        Assert.False(string.IsNullOrWhiteSpace(qrToken));
        Assert.True((await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{slot.Id}"))!.IsBooked);

        var operatorLogin = await LoginWithResponseAsync(operatorAccount.Email, operatorAccount.Password);
        using var operatorClient = operatorLogin.Client;
        Assert.Equal("GridOperator", operatorLogin.Response.Role);
        Assert.Equal(station.Id, operatorLogin.Response.StationId);
        var currentUser = await operatorClient.GetFromJsonAsync<Dictionary<string, JsonElement>>("/api/auth/me");
        Assert.Equal("GridOperator", currentUser!["role"].GetString());
        Assert.Equal(station.Id, currentUser["stationId"].GetString());

        var beforeDashboard = await operatorClient.GetFromJsonAsync<OperatorDashboardResponse>(
            "/api/reports/operator-dashboard");
        Assert.NotNull(beforeDashboard);
        Assert.True(beforeDashboard.ApprovedFutureCount >= 1);
        Assert.Contains(beforeDashboard.UpcomingApproved, item => item.Id == booking.Reservation.Id);
        Assert.DoesNotContain(beforeDashboard.UpcomingApproved, item => item.Id == unrelatedBooking.Reservation.Id);
        Assert.All(beforeDashboard.UpcomingApproved, item => Assert.Equal(station.Id, item.StationId));

        var verification = await operatorClient.PostAsJsonAsync(
            "/api/reservations/verify-qr",
            new { qrToken, stationId = station.Id });
        Assert.Equal(HttpStatusCode.OK, verification.StatusCode);
        var verified = await verification.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.NotNull(verified);
        Assert.Equal(booking.Reservation.Id, verified.Id);
        Assert.Equal("Approved", verified.Status);
        Assert.Equal(
            qrToken,
            (await prosumerClient.GetFromJsonAsync<Dictionary<string, string>>(
                $"/api/reservations/my/{booking.Reservation.Id}/qr"))!["qrToken"]);
        Assert.True((await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{slot.Id}"))!.IsBooked);

        await AssertForbiddenErrorAsync(
            await operatorClient.PutAsync($"/api/reservations/{booking.Reservation.Id}/complete", null),
            "Grid Operators must complete energy transfers through QR verification.");
        var afterRejectedDirect = await admin.GetFromJsonAsync<ReservationResponse>(
            $"/api/reservations/{booking.Reservation.Id}");
        Assert.NotNull(afterRejectedDirect);
        Assert.Equal("Approved", afterRejectedDirect.Status);
        Assert.Null(afterRejectedDirect.CompletedAt);
        Assert.Null(afterRejectedDirect.CompletedBy);
        var storedBeforeScan = await _factory.Database.GetCollection<EnergyReservation>("EnergyReservation")
            .Find(reservation => reservation.Id == booking.Reservation.Id)
            .FirstOrDefaultAsync();
        Assert.Equal(qrToken, storedBeforeScan.QrToken);
        Assert.True((await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{slot.Id}"))!.IsBooked);
        Assert.Equal(
            HttpStatusCode.OK,
            (await operatorClient.PostAsJsonAsync(
                "/api/reservations/verify-qr",
                new { qrToken, stationId = station.Id })).StatusCode);

        var scan = await operatorClient.PostAsJsonAsync(
            "/api/reservations/scan-complete",
            new { qrToken, stationId = station.Id });
        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
        var completed = await scan.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.NotNull(completed);
        Assert.Equal("Completed", completed.Status);
        Assert.NotNull(completed.CompletedAt);
        Assert.Equal(operatorAccount.Email, completed.CompletedBy);
        var storedAfterScan = await _factory.Database.GetCollection<EnergyReservation>("EnergyReservation")
            .Find(reservation => reservation.Id == booking.Reservation.Id)
            .FirstOrDefaultAsync();
        Assert.Null(storedAfterScan.QrToken);
        Assert.Equal(completed.CompletedBy, storedAfterScan.CompletedBy);
        Assert.NotNull(storedAfterScan.CompletedAt);
        Assert.False((await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{slot.Id}"))!.IsBooked);

        var history = await operatorClient.GetFromJsonAsync<PagedResult<ReservationResponse>>(
            "/api/reservations/operator/history");
        Assert.NotNull(history);
        var historyItem = Assert.Single(history.Items);
        Assert.Equal(booking.Reservation.Id, historyItem.Id);
        Assert.Equal("Completed", historyItem.Status);
        Assert.Equal(station.Id, historyItem.StationId);
        Assert.Equal(completed.CompletedAt, historyItem.CompletedAt);
        Assert.Equal(operatorAccount.Email, historyItem.CompletedBy);
        Assert.All(history.Items, item => Assert.Equal("Completed", item.Status));

        var afterDashboard = await operatorClient.GetFromJsonAsync<OperatorDashboardResponse>(
            "/api/reports/operator-dashboard");
        Assert.NotNull(afterDashboard);
        Assert.Equal(1, afterDashboard.CompletedToday);
        Assert.Equal(0, afterDashboard.ApprovedFutureCount);
        Assert.DoesNotContain(afterDashboard.UpcomingApproved, item => item.Id == booking.Reservation.Id);

        var replay = await operatorClient.PostAsJsonAsync(
            "/api/reservations/scan-complete",
            new { qrToken, stationId = station.Id });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        var afterReplay = await admin.GetFromJsonAsync<ReservationResponse>(
            $"/api/reservations/{booking.Reservation.Id}");
        Assert.NotNull(afterReplay);
        Assert.Equal("Completed", afterReplay.Status);
        Assert.Equal(completed.CompletedAt, afterReplay.CompletedAt);
        Assert.Equal(completed.CompletedBy, afterReplay.CompletedBy);
        Assert.False((await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{slot.Id}"))!.IsBooked);
    }

    // Proves an operator cannot discover or mutate reservation, dashboard, QR, or history data at another station.
    [Fact]
    public async Task CrossStationOperator_CannotExposeOrMutateForeignStationWorkflow()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var stationA = await _helpers.CreateStationAsync(admin);
        var stationB = await _helpers.CreateStationAsync(admin);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin, stationA.Id);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var prosumerClient = prosumer.Client;

        var foreign = await CreateApprovedReservationAsync(admin, prosumerClient, stationB.Id, 2);
        var completedForeign = await CreateApprovedReservationAsync(admin, prosumerClient, stationB.Id, 3);
        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PutAsync($"/api/reservations/{completedForeign.Reservation.Id}/complete", null)).StatusCode);

        var scopedList = await operatorClient.GetFromJsonAsync<List<ReservationResponse>>(
            $"/api/reservations?prosumerNic={Uri.EscapeDataString(prosumer.Nic)}");
        Assert.NotNull(scopedList);
        Assert.DoesNotContain(scopedList, item => item.StationId == stationB.Id);
        await AssertForbiddenErrorAsync(
            await operatorClient.GetAsync($"/api/reservations?stationId={stationB.Id}"),
            "Grid Operator is not assigned to the requested station.");

        var scopedSearchResponse = await operatorClient.PostAsJsonAsync("/api/reservations/search", new
        {
            prosumerNic = prosumer.Nic,
            page = 1,
            pageSize = 10,
        });
        Assert.Equal(HttpStatusCode.OK, scopedSearchResponse.StatusCode);
        var scopedSearch = await scopedSearchResponse.Content.ReadFromJsonAsync<PagedResult<ReservationResponse>>();
        Assert.NotNull(scopedSearch);
        Assert.DoesNotContain(scopedSearch.Items, item => item.StationId == stationB.Id);
        await AssertForbiddenErrorAsync(
            await operatorClient.PostAsJsonAsync(
                "/api/reservations/search",
                new { stationId = stationB.Id, prosumerNic = prosumer.Nic }),
            "Grid Operator is not assigned to the requested station.");

        await AssertForbiddenErrorAsync(
            await operatorClient.GetAsync($"/api/reservations/{foreign.Reservation.Id}"),
            "Grid Operator is not assigned to this reservation's station.");
        await AssertForbiddenErrorAsync(
            await operatorClient.GetAsync($"/api/reservations/{foreign.Reservation.Id}/qr"),
            "Grid Operator is not assigned to this reservation's station.");
        await AssertForbiddenErrorAsync(
            await operatorClient.PostAsJsonAsync(
                "/api/reservations/verify-qr",
                new { qrToken = foreign.QrToken, stationId = stationB.Id }),
            "Grid Operator is not assigned to the requested station.");
        await AssertForbiddenErrorAsync(
            await operatorClient.PostAsJsonAsync(
                "/api/reservations/scan-complete",
                new { qrToken = foreign.QrToken, stationId = stationB.Id }),
            "Grid Operator is not assigned to the requested station.");

        var ownDashboard = await operatorClient.GetFromJsonAsync<OperatorDashboardResponse>(
            "/api/reports/operator-dashboard");
        Assert.NotNull(ownDashboard);
        Assert.Equal(0, ownDashboard.CompletedToday);
        Assert.DoesNotContain(ownDashboard.UpcomingApproved, item => item.StationId == stationB.Id);
        await AssertForbiddenErrorAsync(
            await operatorClient.GetAsync($"/api/reports/operator-dashboard?stationId={stationB.Id}"),
            "Grid Operator is not assigned to the requested station.");

        var ownHistory = await operatorClient.GetFromJsonAsync<PagedResult<ReservationResponse>>(
            "/api/reservations/operator/history");
        Assert.NotNull(ownHistory);
        Assert.DoesNotContain(ownHistory.Items, item => item.Id == completedForeign.Reservation.Id);
        await AssertForbiddenErrorAsync(
            await operatorClient.GetAsync($"/api/reservations/operator/history?stationId={stationB.Id}"),
            "Grid Operator is not assigned to the requested station.");

        var unchanged = await admin.GetFromJsonAsync<ReservationResponse>(
            $"/api/reservations/{foreign.Reservation.Id}");
        Assert.NotNull(unchanged);
        Assert.Equal("Approved", unchanged.Status);
        Assert.Null(unchanged.CompletedAt);
        Assert.Null(unchanged.CompletedBy);
        var stored = await _factory.Database.GetCollection<EnergyReservation>("EnergyReservation")
            .Find(reservation => reservation.Id == foreign.Reservation.Id)
            .FirstOrDefaultAsync();
        Assert.Equal(foreign.QrToken, stored.QrToken);
        Assert.True((await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{foreign.Slot.Id}"))!.IsBooked);
    }

    // Proves unassigned operators can authenticate and refresh identity but cannot use station-scoped operations.
    [Fact]
    public async Task UnassignedOperator_AuthenticatesWithNullStationButOperationalCallsAreForbidden()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        var unassignedAccount = await _helpers.CreateGridOperatorAsync(admin);
        using var initialOperatorClient = unassignedAccount.Client;
        using var prosumerClient = prosumer.Client;
        var approved = await CreateApprovedReservationAsync(admin, prosumerClient, station.Id, 2);

        var login = await LoginWithResponseAsync(unassignedAccount.Email, unassignedAccount.Password);
        using var operatorClient = login.Client;
        Assert.Equal("GridOperator", login.Response.Role);
        Assert.Null(login.Response.StationId);
        var currentUser = await operatorClient.GetFromJsonAsync<Dictionary<string, JsonElement>>("/api/auth/me");
        Assert.NotNull(currentUser);
        Assert.Equal(JsonValueKind.Null, currentUser["stationId"].ValueKind);

        var requests = new Func<Task<HttpResponseMessage>>[]
        {
            () => operatorClient.GetAsync("/api/reports/operator-dashboard"),
            () => operatorClient.GetAsync("/api/reservations/operator/history"),
            () => operatorClient.GetAsync("/api/reservations"),
            () => operatorClient.GetAsync($"/api/reservations/{approved.Reservation.Id}"),
            () => operatorClient.PostAsJsonAsync(
                "/api/reservations/verify-qr",
                new { qrToken = approved.QrToken, stationId = station.Id }),
            () => operatorClient.PostAsJsonAsync(
                "/api/reservations/scan-complete",
                new { qrToken = approved.QrToken, stationId = station.Id }),
        };

        foreach (var request in requests)
        {
            await AssertForbiddenErrorAsync(
                await request(),
                "Grid Operator is not assigned to a station.");
        }

        var unchanged = await _factory.Database.GetCollection<EnergyReservation>("EnergyReservation")
            .Find(reservation => reservation.Id == approved.Reservation.Id)
            .FirstOrDefaultAsync();
        Assert.Equal("Approved", unchanged.Status);
        Assert.Equal(approved.QrToken, unchanged.QrToken);
        Assert.True((await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{approved.Slot.Id}"))!.IsBooked);
    }

    // Proves the same JWT immediately follows a persisted Backoffice reassignment rather than stale station data.
    [Fact]
    public async Task ReassignedOperator_SameJwtImmediatelyUsesNewPersistedStation()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var stationA = await _helpers.CreateStationAsync(admin);
        var stationB = await _helpers.CreateStationAsync(admin);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin, stationA.Id);
        using var initialOperatorClient = operatorAccount.Client;
        using var prosumerClient = prosumer.Client;
        var reservationA = await CreateApprovedReservationAsync(admin, prosumerClient, stationA.Id, 2);
        var reservationB = await CreateApprovedReservationAsync(admin, prosumerClient, stationB.Id, 3);

        var login = await LoginWithResponseAsync(operatorAccount.Email, operatorAccount.Password);
        using var operatorClient = login.Client;
        Assert.Equal(stationA.Id, login.Response.StationId);
        var originalToken = operatorClient.DefaultRequestHeaders.Authorization!.Parameter;
        Assert.Equal(
            HttpStatusCode.OK,
            (await operatorClient.PostAsJsonAsync(
                "/api/reservations/verify-qr",
                new { qrToken = reservationA.QrToken, stationId = stationA.Id })).StatusCode);

        var operators = await admin.GetFromJsonAsync<List<UserResponse>>("/api/users?role=GridOperator");
        var user = Assert.Single(operators!, candidate => candidate.Email == operatorAccount.Email);
        var reassignment = await admin.PutAsJsonAsync(
            $"/api/users/{user.Id}",
            new { stationId = stationB.Id });
        Assert.Equal(HttpStatusCode.OK, reassignment.StatusCode);
        Assert.Equal(
            stationB.Id,
            (await reassignment.Content.ReadFromJsonAsync<UserResponse>())!.StationId);
        Assert.Equal(originalToken, operatorClient.DefaultRequestHeaders.Authorization!.Parameter);

        var refreshed = await operatorClient.GetFromJsonAsync<Dictionary<string, JsonElement>>("/api/auth/me");
        Assert.Equal(stationB.Id, refreshed!["stationId"].GetString());
        await AssertForbiddenErrorAsync(
            await operatorClient.GetAsync($"/api/reports/operator-dashboard?stationId={stationA.Id}"),
            "Grid Operator is not assigned to the requested station.");
        await AssertForbiddenErrorAsync(
            await operatorClient.GetAsync($"/api/reservations?stationId={stationA.Id}"),
            "Grid Operator is not assigned to the requested station.");
        await AssertForbiddenErrorAsync(
            await operatorClient.PostAsJsonAsync(
                "/api/reservations/verify-qr",
                new { qrToken = reservationA.QrToken, stationId = stationA.Id }),
            "Grid Operator is not assigned to the requested station.");

        var stationBList = await operatorClient.GetFromJsonAsync<List<ReservationResponse>>(
            $"/api/reservations?stationId={stationB.Id}");
        Assert.Contains(stationBList!, item => item.Id == reservationB.Reservation.Id);
        Assert.DoesNotContain(stationBList!, item => item.Id == reservationA.Reservation.Id);
        var stationBDashboard = await operatorClient.GetFromJsonAsync<OperatorDashboardResponse>(
            "/api/reports/operator-dashboard");
        Assert.NotNull(stationBDashboard);
        Assert.Contains(stationBDashboard.UpcomingApproved, item => item.Id == reservationB.Reservation.Id);
        Assert.DoesNotContain(stationBDashboard.UpcomingApproved, item => item.Id == reservationA.Reservation.Id);
        Assert.Equal(
            HttpStatusCode.OK,
            (await operatorClient.PostAsJsonAsync(
                "/api/reservations/verify-qr",
                new { qrToken = reservationB.QrToken, stationId = stationB.Id })).StatusCode);
    }

    // Proves Backoffice administration and Prosumer self-service remain compatible with operator-only boundaries.
    [Fact]
    public async Task BackofficeAndProsumer_CurrentAdministrativeAndSelfServiceBehaviorRemainsIntact()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        using var anonymous = _factory.CreateClient();
        var station = await _helpers.CreateStationAsync(admin);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var prosumerClient = prosumer.Client;
        var approved = await CreateApprovedReservationAsync(admin, prosumerClient, station.Id, 2);

        Assert.Equal(
            approved.Reservation.Id,
            (await prosumerClient.GetFromJsonAsync<ReservationResponse>(
                $"/api/reservations/my/{approved.Reservation.Id}"))!.Id);
        Assert.Equal(
            approved.QrToken,
            (await prosumerClient.GetFromJsonAsync<Dictionary<string, string>>(
                $"/api/reservations/my/{approved.Reservation.Id}/qr"))!["qrToken"]);
        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.GetAsync("/api/reports/operator-dashboard")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.GetAsync($"/api/reservations/{approved.Reservation.Id}")).StatusCode);

        var prosumerRestrictedRequests = new Func<Task<HttpResponseMessage>>[]
        {
            () => prosumerClient.GetAsync("/api/reports/operator-dashboard"),
            () => prosumerClient.GetAsync("/api/reservations/operator/history"),
            () => prosumerClient.PostAsJsonAsync(
                "/api/reservations/verify-qr",
                new { qrToken = approved.QrToken, stationId = station.Id }),
            () => prosumerClient.PostAsJsonAsync(
                "/api/reservations/scan-complete",
                new { qrToken = approved.QrToken, stationId = station.Id }),
            () => prosumerClient.PutAsync($"/api/reservations/{approved.Reservation.Id}/complete", null),
        };
        foreach (var request in prosumerRestrictedRequests)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await request()).StatusCode);
        }

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await admin.PostAsJsonAsync(
                "/api/reservations/scan-complete",
                new { qrToken = approved.QrToken, stationId = station.Id })).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/reports/operator-dashboard")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync(
                "/api/reservations/scan-complete",
                new { qrToken = approved.QrToken, stationId = station.Id })).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PutAsync($"/api/reservations/{approved.Reservation.Id}/complete", null)).StatusCode);

        var directCompletion = await admin.PutAsync(
            $"/api/reservations/{approved.Reservation.Id}/complete",
            null);
        Assert.Equal(HttpStatusCode.OK, directCompletion.StatusCode);
        var completed = await directCompletion.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.NotNull(completed);
        Assert.Equal("Completed", completed.Status);
        Assert.Equal("admin@smartsolar.com", completed.CompletedBy);
        Assert.Equal(
            "Completed",
            (await prosumerClient.GetFromJsonAsync<ReservationResponse>(
                $"/api/reservations/my/{approved.Reservation.Id}"))!.Status);
    }

    // Proves the generated OpenAPI document still publishes every documented Member 4 route.
    [Fact]
    public void OpenApiContract_PublishesAllDocumentedMember4Paths()
    {
        using var scope = _factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISwaggerProvider>();
        var document = provider.GetSwagger("v1");
        var expectedPaths = new[]
        {
            "/api/auth/login",
            "/api/auth/me",
            "/api/reports/operator-dashboard",
            "/api/reservations/operator/history",
            "/api/reservations/{id}/qr",
            "/api/reservations/verify-qr",
            "/api/reservations/scan-complete",
            "/api/reservations/{id}/complete",
        };

        Assert.All(expectedPaths, path => Assert.Contains(path, document.Paths.Keys));
    }

    // Logs in through the public endpoint and returns both the authorized client and its response contract.
    private async Task<(HttpClient Client, LoginResponse Response)> LoginWithResponseAsync(
        string email,
        string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        return (client, login);
    }

    // Creates and approves a reservation entirely through public APIs, then retrieves its live QR token.
    private async Task<(ReservationResponse Reservation, SlotResponse Slot, string QrToken)>
        CreateApprovedReservationAsync(
            HttpClient admin,
            HttpClient prosumer,
            string stationId,
            double hoursAhead)
    {
        var slot = await _helpers.CreateSlotAsync(admin, stationId, hoursAhead);
        var booking = await _helpers.BookAsync(prosumer, stationId, slot.Id);
        await _helpers.ApproveAsync(admin, booking.Reservation.Id);
        var reservation = await admin.GetFromJsonAsync<ReservationResponse>(
            $"/api/reservations/{booking.Reservation.Id}");
        var qr = await prosumer.GetFromJsonAsync<Dictionary<string, string>>(
            $"/api/reservations/my/{booking.Reservation.Id}/qr");
        Assert.NotNull(reservation);
        Assert.Equal("Approved", reservation.Status);
        Assert.NotNull(qr);
        return (reservation, slot, qr["qrToken"]);
    }

    // Asserts the established Member 4 forbidden JSON contract.
    private static async Task AssertForbiddenErrorAsync(
        HttpResponseMessage response,
        string expectedError)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(body);
        Assert.Equal(expectedError, body["error"]);
    }
}
