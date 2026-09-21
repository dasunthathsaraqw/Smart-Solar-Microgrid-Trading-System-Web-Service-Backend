/**
 * File: OperatorFlowTests.cs
 * Purpose: Integration checks for QR-based operator completion, replay protection and role gates.
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
public sealed class OperatorFlowTests
{
    private readonly TestHelpers _helpers;

    // Initializes operator-flow tests against the shared disposable API database.
    public OperatorFlowTests(ApiFactory factory)
    {
        _helpers = new TestHelpers(factory);
    }

    // Rule: a valid operator scan completes an approved booking, frees the slot and rejects replay.
    [Fact]
    public async Task ScanComplete_ValidQr_CompletesFreesSlotAndRejectsReplay()
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
        var scan = await operatorClient.PostAsJsonAsync("/api/reservations/scan-complete", new { qrToken = token, stationId = station.Id });
        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
        var completed = await scan.Content.ReadFromJsonAsync<ReservationResponse>();
        Assert.Equal("Completed", completed!.Status);
        Assert.Equal(operatorAccount.Email, completed.CompletedBy);
        Assert.False((await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{slot.Id}"))!.IsBooked);
        var replay = await operatorClient.PostAsJsonAsync("/api/reservations/scan-complete", new { qrToken = token, stationId = station.Id });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    // Rule: presenting a valid QR at the wrong station leaves the reservation Approved.
    [Fact]
    public async Task ScanComplete_WrongStation_RejectsWithoutChangingReservation()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var prosumerClient = prosumer.Client;
        var station = await _helpers.CreateStationAsync(admin);
        var otherStation = await _helpers.CreateStationAsync(admin);
        var slot = await _helpers.CreateSlotAsync(admin, station.Id, 2);
        var booking = await _helpers.BookAsync(prosumerClient, station.Id, slot.Id);
        await _helpers.ApproveAsync(admin, booking.Reservation.Id);
        var token = (await prosumerClient.GetFromJsonAsync<Dictionary<string, string>>($"/api/reservations/my/{booking.Reservation.Id}/qr"))!["qrToken"];
        var rejected = await operatorClient.PostAsJsonAsync("/api/reservations/scan-complete", new { qrToken = token, stationId = otherStation.Id });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal("Approved", (await admin.GetFromJsonAsync<ReservationResponse>($"/api/reservations/{booking.Reservation.Id}"))!.Status);
        Assert.True((await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{slot.Id}"))!.IsBooked);
    }

    // Rule: two simultaneous scans can claim an Approved reservation only once.
    [Fact]
    public async Task ScanComplete_TwoConcurrentRequests_ExactlyOneSucceeds()
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
        var first = operatorClient.PostAsJsonAsync("/api/reservations/scan-complete", new { qrToken = token, stationId = station.Id });
        var second = operatorClient.PostAsJsonAsync("/api/reservations/scan-complete", new { qrToken = token, stationId = station.Id });
        var responses = await Task.WhenAll(first, second);
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest }.Order(), responses.Select(response => response.StatusCode).Order());
        Assert.False((await admin.GetFromJsonAsync<SlotResponse>($"/api/slots/{slot.Id}"))!.IsBooked);
    }

    // Rule: only GridOperators can scan and complete, not Prosumers or Backoffice users.
    [Fact]
    public async Task ScanComplete_ProsumerOrBackoffice_ReturnsForbidden()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var prosumerClient = prosumer.Client;
        var station = await _helpers.CreateStationAsync(admin);
        var slot = await _helpers.CreateSlotAsync(admin, station.Id, 2);
        var booking = await _helpers.BookAsync(prosumerClient, station.Id, slot.Id);
        await _helpers.ApproveAsync(admin, booking.Reservation.Id);
        var token = (await prosumerClient.GetFromJsonAsync<Dictionary<string, string>>($"/api/reservations/my/{booking.Reservation.Id}/qr"))!["qrToken"];
        var prosumerResponse = await prosumerClient.PostAsJsonAsync("/api/reservations/scan-complete", new { qrToken = token, stationId = station.Id });
        var backofficeResponse = await admin.PostAsJsonAsync("/api/reservations/scan-complete", new { qrToken = token, stationId = station.Id });
        Assert.Equal(HttpStatusCode.Forbidden, prosumerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, backofficeResponse.StatusCode);
    }
}
