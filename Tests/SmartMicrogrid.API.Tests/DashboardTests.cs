/**
 * File: DashboardTests.cs
 * Purpose: Integration checks for owner-scoped prosumer counts and station-scoped operator activity.
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
public sealed class DashboardTests
{
    private readonly TestHelpers _helpers;

    // Initializes dashboard tests against the shared disposable API database.
    public DashboardTests(ApiFactory factory)
    {
        _helpers = new TestHelpers(factory);
    }

    // Rule: the Prosumer dashboard counts only the caller's hand-created reservation statuses.
    [Fact]
    public async Task GetMyDashboard_MixedStatusesAndAnotherOwner_ReturnsOwnedCounts()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var first = await _helpers.RegisterAndApproveProsumerAsync(admin);
        var second = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var firstClient = first.Client;
        using var secondClient = second.Client;
        var station = await _helpers.CreateStationAsync(admin);
        var pendingSlot = await _helpers.CreateSlotAsync(admin, station.Id, 30);
        var approvedSlot = await _helpers.CreateSlotAsync(admin, station.Id, 40);
        var completedSlot = await _helpers.CreateSlotAsync(admin, station.Id, 2);
        var cancelledSlot = await _helpers.CreateSlotAsync(admin, station.Id, 50);
        var otherSlot = await _helpers.CreateSlotAsync(admin, station.Id, 60);
        await _helpers.BookAsync(firstClient, station.Id, pendingSlot.Id);
        var approved = await _helpers.BookAsync(firstClient, station.Id, approvedSlot.Id);
        await _helpers.ApproveAsync(admin, approved.Reservation.Id);
        var completed = await _helpers.BookAsync(firstClient, station.Id, completedSlot.Id);
        await _helpers.ApproveAsync(admin, completed.Reservation.Id);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/reservations/{completed.Reservation.Id}/complete", null)).StatusCode);
        var cancelled = await _helpers.BookAsync(firstClient, station.Id, cancelledSlot.Id);
        Assert.Equal(HttpStatusCode.OK, (await firstClient.PutAsJsonAsync($"/api/reservations/my/{cancelled.Reservation.Id}/cancel", new { reason = "Changed plans" })).StatusCode);
        await _helpers.BookAsync(secondClient, station.Id, otherSlot.Id);

        var own = await firstClient.GetFromJsonAsync<ProsumerDashboardResponse>("/api/reports/my-dashboard");
        var other = await secondClient.GetFromJsonAsync<ProsumerDashboardResponse>("/api/reports/my-dashboard");
        Assert.NotNull(own);
        Assert.Equal(1, own.PendingCount);
        Assert.Equal(1, own.ApprovedFutureCount);
        Assert.Equal(1, own.CompletedCount);
        Assert.Equal(1, own.CancelledCount);
        Assert.Equal(approved.Reservation.Id, own.NextReservation!.Id);
        Assert.Equal(1, other!.PendingCount);
        Assert.Equal(0, other.ApprovedFutureCount);
        Assert.Equal(0, other.CompletedCount);
        Assert.Equal(0, other.CancelledCount);
    }

    // Rule: operator dashboard counts UTC-today and future reservations for its selected station only.
    [Fact]
    public async Task GetOperatorDashboard_StationScope_ReturnsTodayAndFutureCounts()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var prosumerClient = prosumer.Client;
        var station = await _helpers.CreateStationAsync(admin);
        var unrelatedStation = await _helpers.CreateStationAsync(admin);
        var pendingSlot = await _helpers.CreateSlotAsync(admin, station.Id, 2);
        var approvedSlot = await _helpers.CreateSlotAsync(admin, station.Id, 3);
        var completedSlot = await _helpers.CreateSlotAsync(admin, station.Id, 4);
        var unrelatedSlot = await _helpers.CreateSlotAsync(admin, unrelatedStation.Id, 5);
        await _helpers.BookAsync(prosumerClient, station.Id, pendingSlot.Id);
        var approved = await _helpers.BookAsync(prosumerClient, station.Id, approvedSlot.Id);
        await _helpers.ApproveAsync(admin, approved.Reservation.Id);
        var completed = await _helpers.BookAsync(prosumerClient, station.Id, completedSlot.Id);
        await _helpers.ApproveAsync(admin, completed.Reservation.Id);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/reservations/{completed.Reservation.Id}/complete", null)).StatusCode);
        var unrelated = await _helpers.BookAsync(prosumerClient, unrelatedStation.Id, unrelatedSlot.Id);
        await _helpers.ApproveAsync(admin, unrelated.Reservation.Id);

        var response = await operatorClient.GetAsync($"/api/reports/operator-dashboard?stationId={station.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = await response.Content.ReadFromJsonAsync<OperatorDashboardResponse>();
        Assert.NotNull(dashboard);
        var utcToday = DateTime.UtcNow.Date;
        Assert.Equal(pendingSlot.StartTime.Date == utcToday ? 1 : 0, dashboard.PendingToday);
        Assert.Equal(approvedSlot.StartTime.Date == utcToday ? 1 : 0, dashboard.ApprovedToday);
        Assert.Equal(1, dashboard.CompletedToday);
        Assert.Equal(1, dashboard.ApprovedFutureCount);
        Assert.Equal(approved.Reservation.Id, Assert.Single(dashboard.UpcomingApproved).Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await operatorClient.GetAsync("/api/reports/operator-dashboard?stationId=000000000000000000000000")).StatusCode);
    }
}
