/**
 * File: OperatorFlowTests.cs
 * Purpose: Integration checks for QR-based operator completion, replay protection and role gates.
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
public sealed class OperatorFlowTests
{
    private readonly ApiFactory _factory;
    private readonly TestHelpers _helpers;

    // Initializes operator-flow tests against the shared disposable API database.
    public OperatorFlowTests(ApiFactory factory)
    {
        _factory = factory;
        _helpers = new TestHelpers(factory);
    }

    // Rule: operator history includes only completed transactions and honors station, date, order and paging filters.
    [Fact]
    public async Task OperatorHistory_CompletedTransactions_AreFilteredSortedAndPaged()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var prosumerClient = prosumer.Client;
        var station = await _helpers.CreateStationAsync(admin);
        var otherStation = await _helpers.CreateStationAsync(admin);

        var olderCompleted = await CreateReservationWithStatusAsync(admin, prosumerClient, station.Id, 2, "Completed");
        var newerCompleted = await CreateReservationWithStatusAsync(admin, prosumerClient, station.Id, 3, "Completed");
        await CreateReservationWithStatusAsync(admin, prosumerClient, station.Id, 4, "Pending");
        await CreateReservationWithStatusAsync(admin, prosumerClient, station.Id, 5, "Approved");
        await CreateReservationWithStatusAsync(admin, prosumerClient, station.Id, 6, "Cancelled");
        await CreateReservationWithStatusAsync(admin, prosumerClient, otherStation.Id, 7, "Completed");

        var now = DateTime.UtcNow;
        var olderCompletedAt = now.AddDays(-4);
        var newerCompletedAt = now.AddDays(-2);
        var reservations = _factory.Database.GetCollection<EnergyReservation>("EnergyReservation");
        await reservations.UpdateOneAsync(
            reservation => reservation.Id == olderCompleted.Id,
            Builders<EnergyReservation>.Update.Set(reservation => reservation.CompletedAt, olderCompletedAt));
        await reservations.UpdateOneAsync(
            reservation => reservation.Id == newerCompleted.Id,
            Builders<EnergyReservation>.Update.Set(reservation => reservation.CompletedAt, newerCompletedAt));

        var history = await operatorClient.GetFromJsonAsync<PagedResult<ReservationResponse>>(
            $"/api/reservations/operator/history?stationId={station.Id}");
        Assert.NotNull(history);
        Assert.Equal(2, history.TotalCount);
        Assert.All(history.Items, item => Assert.Equal("Completed", item.Status));
        Assert.Equal(new[] { newerCompleted.Id, olderCompleted.Id }, history.Items.Select(item => item.Id));

        var dateFrom = Uri.EscapeDataString(now.AddDays(-3).ToString("O"));
        var dateTo = Uri.EscapeDataString(now.AddDays(-1).ToString("O"));
        var dateFiltered = await operatorClient.GetFromJsonAsync<PagedResult<ReservationResponse>>(
            $"/api/reservations/operator/history?stationId={station.Id}&dateFrom={dateFrom}&dateTo={dateTo}");
        Assert.NotNull(dateFiltered);
        Assert.Single(dateFiltered.Items);
        Assert.Equal(newerCompleted.Id, dateFiltered.Items[0].Id);

        var firstPage = await operatorClient.GetFromJsonAsync<PagedResult<ReservationResponse>>(
            $"/api/reservations/operator/history?stationId={station.Id}&page=1&pageSize=1");
        var secondPage = await operatorClient.GetFromJsonAsync<PagedResult<ReservationResponse>>(
            $"/api/reservations/operator/history?stationId={station.Id}&page=2&pageSize=1");
        Assert.NotNull(firstPage);
        Assert.NotNull(secondPage);
        Assert.Equal(newerCompleted.Id, Assert.Single(firstPage.Items).Id);
        Assert.Equal(olderCompleted.Id, Assert.Single(secondPage.Items).Id);
        Assert.True(firstPage.HasNextPage);
        Assert.True(secondPage.HasPreviousPage);
    }

    // Rule: transaction history is strictly GridOperator-only.
    [Fact]
    public async Task OperatorHistory_ProsumerBackofficeOrAnonymous_IsRejected()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var prosumerClient = prosumer.Client;
        using var anonymousClient = _factory.CreateClient();

        var prosumerResponse = await prosumerClient.GetAsync("/api/reservations/operator/history");
        var backofficeResponse = await admin.GetAsync("/api/reservations/operator/history");
        var anonymousResponse = await anonymousClient.GetAsync("/api/reservations/operator/history");

        Assert.Equal(HttpStatusCode.Forbidden, prosumerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, backofficeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
    }

    // Rule: invalid paging, date ranges and station filters return BadRequest, while no matches return an empty page.
    [Fact]
    public async Task OperatorHistory_ValidationAndEmptyResults_FollowApiConventions()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin);
        using var operatorClient = operatorAccount.Client;
        var emptyStation = await _helpers.CreateStationAsync(admin);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await operatorClient.GetAsync("/api/reservations/operator/history?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await operatorClient.GetAsync("/api/reservations/operator/history?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await operatorClient.GetAsync("/api/reservations/operator/history?dateFrom=2026-02-02&dateTo=2026-02-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await operatorClient.GetAsync("/api/reservations/operator/history?stationId=not-an-object-id")).StatusCode);

        var empty = await operatorClient.GetFromJsonAsync<PagedResult<ReservationResponse>>(
            $"/api/reservations/operator/history?stationId={emptyStation.Id}");
        Assert.NotNull(empty);
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalCount);
        Assert.Equal(0, empty.TotalPages);
    }

    // Creates a reservation through public APIs and advances it to the requested lifecycle status.
    private async Task<ReservationResponse> CreateReservationWithStatusAsync(
        HttpClient admin,
        HttpClient prosumer,
        string stationId,
        double hoursAhead,
        string status)
    {
        var slot = await _helpers.CreateSlotAsync(admin, stationId, hoursAhead);
        var booking = await _helpers.BookAsync(prosumer, stationId, slot.Id);

        if (status is "Approved" or "Completed")
        {
            await _helpers.ApproveAsync(admin, booking.Reservation.Id);
        }

        if (status == "Completed")
        {
            var completion = await admin.PutAsync($"/api/reservations/{booking.Reservation.Id}/complete", null);
            Assert.Equal(HttpStatusCode.OK, completion.StatusCode);
        }
        else if (status == "Cancelled")
        {
            var cancellation = await admin.PutAsJsonAsync(
                $"/api/reservations/{booking.Reservation.Id}/cancel",
                new { reason = "Operator history test" });
            Assert.Equal(HttpStatusCode.OK, cancellation.StatusCode);
        }

        var response = await admin.GetFromJsonAsync<ReservationResponse>($"/api/reservations/{booking.Reservation.Id}");
        Assert.NotNull(response);
        Assert.Equal(status, response.Status);
        return response;
    }

    // Rule: a valid operator scan completes an approved booking, frees the slot and rejects replay.
    [Fact]
    public async Task ScanComplete_ValidQr_CompletesFreesSlotAndRejectsReplay()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin, station.Id);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var prosumerClient = prosumer.Client;
        var slot = await _helpers.CreateSlotAsync(admin, station.Id, 2);
        var booking = await _helpers.BookAsync(prosumerClient, station.Id, slot.Id);
        await _helpers.ApproveAsync(admin, booking.Reservation.Id);
        var token = (await prosumerClient.GetFromJsonAsync<Dictionary<string, string>>($"/api/reservations/my/{booking.Reservation.Id}/qr"))!["qrToken"];
        var scan = await operatorClient.PostAsJsonAsync("/api/reservations/scan-complete", new { qrToken = token, stationId = station.Id });
        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
        var completed = await scan.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.Equal("Completed", completed!.Status);
        Assert.Equal(operatorAccount.Email, completed.CompletedBy);
        Assert.False((await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{slot.Id}"))!.IsBooked);
        var replay = await operatorClient.PostAsJsonAsync("/api/reservations/scan-complete", new { qrToken = token, stationId = station.Id });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    // Rule: an assigned operator cannot verify or complete at another station, leaving QR, reservation and slot unchanged.
    [Fact]
    public async Task QrEndpoints_RequestForAnotherStation_RejectBeforeChangingReservation()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var otherStation = await _helpers.CreateStationAsync(admin);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin, station.Id);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var prosumerClient = prosumer.Client;
        var slot = await _helpers.CreateSlotAsync(admin, station.Id, 2);
        var booking = await _helpers.BookAsync(prosumerClient, station.Id, slot.Id);
        await _helpers.ApproveAsync(admin, booking.Reservation.Id);
        var token = (await prosumerClient.GetFromJsonAsync<Dictionary<string, string>>($"/api/reservations/my/{booking.Reservation.Id}/qr"))!["qrToken"];

        var verifyRejected = await operatorClient.PostAsJsonAsync(
            "/api/reservations/verify-qr",
            new { qrToken = token, stationId = otherStation.Id });
        var rejected = await operatorClient.PostAsJsonAsync("/api/reservations/scan-complete", new { qrToken = token, stationId = otherStation.Id });
        Assert.Equal(HttpStatusCode.Forbidden, verifyRejected.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        Assert.Equal("Approved", (await admin.GetFromJsonAsync<ReservationResponse>($"/api/reservations/{booking.Reservation.Id}"))!.Status);
        var stored = await _factory.Database.GetCollection<EnergyReservation>("EnergyReservation")
            .Find(reservation => reservation.Id == booking.Reservation.Id)
            .FirstOrDefaultAsync();
        Assert.Equal(token, stored.QrToken);
        Assert.True((await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{slot.Id}"))!.IsBooked);
    }

    // Rule: an assigned operator can verify an Approved QR at the assigned station.
    [Fact]
    public async Task VerifyQr_AssignedStation_ReturnsApprovedReservation()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin, station.Id);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var prosumerClient = prosumer.Client;
        var slot = await _helpers.CreateSlotAsync(admin, station.Id, 2);
        var booking = await _helpers.BookAsync(prosumerClient, station.Id, slot.Id);
        await _helpers.ApproveAsync(admin, booking.Reservation.Id);
        var token = (await prosumerClient.GetFromJsonAsync<Dictionary<string, string>>($"/api/reservations/my/{booking.Reservation.Id}/qr"))!["qrToken"];

        var response = await operatorClient.PostAsJsonAsync(
            "/api/reservations/verify-qr",
            new { qrToken = token, stationId = station.Id });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var verified = await response.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.Equal(booking.Reservation.Id, verified!.Id);
        Assert.Equal("Approved", verified.Status);
    }

    // Rule: assignment authorization passes first, while the existing QR-to-reservation station check still rejects a foreign QR.
    [Fact]
    public async Task VerifyQr_AssignedStationButForeignReservation_PreservesExistingStationValidation()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var assignedStation = await _helpers.CreateStationAsync(admin);
        var reservationStation = await _helpers.CreateStationAsync(admin);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin, assignedStation.Id);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var prosumerClient = prosumer.Client;
        var slot = await _helpers.CreateSlotAsync(admin, reservationStation.Id, 2);
        var booking = await _helpers.BookAsync(prosumerClient, reservationStation.Id, slot.Id);
        await _helpers.ApproveAsync(admin, booking.Reservation.Id);
        var token = (await prosumerClient.GetFromJsonAsync<Dictionary<string, string>>($"/api/reservations/my/{booking.Reservation.Id}/qr"))!["qrToken"];

        var response = await operatorClient.PostAsJsonAsync(
            "/api/reservations/verify-qr",
            new { qrToken = token, stationId = assignedStation.Id });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("QR does not belong to this station", error!["error"]);
        Assert.Equal("Approved", (await admin.GetFromJsonAsync<ReservationResponse>($"/api/reservations/{booking.Reservation.Id}"))!.Status);
        Assert.True((await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{slot.Id}"))!.IsBooked);
    }

    // Rule: an unassigned GridOperator cannot verify or complete a QR at any station.
    [Fact]
    public async Task QrEndpoints_UnassignedOperator_ReturnForbiddenWithoutChangingReservation()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var prosumerClient = prosumer.Client;
        var station = await _helpers.CreateStationAsync(admin);
        var slot = await _helpers.CreateSlotAsync(admin, station.Id, 2);
        var booking = await _helpers.BookAsync(prosumerClient, station.Id, slot.Id);
        await _helpers.ApproveAsync(admin, booking.Reservation.Id);
        var token = (await prosumerClient.GetFromJsonAsync<Dictionary<string, string>>($"/api/reservations/my/{booking.Reservation.Id}/qr"))!["qrToken"];

        var verify = await operatorClient.PostAsJsonAsync(
            "/api/reservations/verify-qr",
            new { qrToken = token, stationId = station.Id });
        var complete = await operatorClient.PostAsJsonAsync(
            "/api/reservations/scan-complete",
            new { qrToken = token, stationId = station.Id });
        Assert.Equal(HttpStatusCode.Forbidden, verify.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, complete.StatusCode);

        var stored = await _factory.Database.GetCollection<EnergyReservation>("EnergyReservation")
            .Find(reservation => reservation.Id == booking.Reservation.Id)
            .FirstOrDefaultAsync();
        Assert.Equal("Approved", stored.Status);
        Assert.Equal(token, stored.QrToken);
        Assert.True((await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{slot.Id}"))!.IsBooked);
    }

    // Rule: two simultaneous scans can claim an Approved reservation only once.
    [Fact]
    public async Task ScanComplete_TwoConcurrentRequests_ExactlyOneSucceeds()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var station = await _helpers.CreateStationAsync(admin);
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin, station.Id);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var prosumerClient = prosumer.Client;
        var slot = await _helpers.CreateSlotAsync(admin, station.Id, 2);
        var booking = await _helpers.BookAsync(prosumerClient, station.Id, slot.Id);
        await _helpers.ApproveAsync(admin, booking.Reservation.Id);
        var token = (await prosumerClient.GetFromJsonAsync<Dictionary<string, string>>($"/api/reservations/my/{booking.Reservation.Id}/qr"))!["qrToken"];
        var first = operatorClient.PostAsJsonAsync("/api/reservations/scan-complete", new { qrToken = token, stationId = station.Id });
        var second = operatorClient.PostAsJsonAsync("/api/reservations/scan-complete", new { qrToken = token, stationId = station.Id });
        var responses = await Task.WhenAll(first, second);
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest }.Order(), responses.Select(response => response.StatusCode).Order());
        Assert.False((await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{slot.Id}"))!.IsBooked);
    }

    // Rule: only GridOperators can scan and complete, not Prosumers, Backoffice users or anonymous callers.
    [Fact]
    public async Task ScanComplete_ProsumerBackofficeOrAnonymous_IsRejected()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var prosumerClient = prosumer.Client;
        using var anonymousClient = _factory.CreateClient();
        var station = await _helpers.CreateStationAsync(admin);
        var slot = await _helpers.CreateSlotAsync(admin, station.Id, 2);
        var booking = await _helpers.BookAsync(prosumerClient, station.Id, slot.Id);
        await _helpers.ApproveAsync(admin, booking.Reservation.Id);
        var token = (await prosumerClient.GetFromJsonAsync<Dictionary<string, string>>($"/api/reservations/my/{booking.Reservation.Id}/qr"))!["qrToken"];
        var prosumerResponse = await prosumerClient.PostAsJsonAsync("/api/reservations/scan-complete", new { qrToken = token, stationId = station.Id });
        var backofficeResponse = await admin.PostAsJsonAsync("/api/reservations/scan-complete", new { qrToken = token, stationId = station.Id });
        var anonymousResponse = await anonymousClient.PostAsJsonAsync("/api/reservations/scan-complete", new { qrToken = token, stationId = station.Id });
        Assert.Equal(HttpStatusCode.Forbidden, prosumerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, backofficeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
    }
}
