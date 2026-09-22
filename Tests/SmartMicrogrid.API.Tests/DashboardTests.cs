/**
 * File: DashboardTests.cs
 * Purpose: Integration checks for owner-scoped prosumer counts and station-scoped operator activity.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using System.Net;
using System.Net.Http.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using SmartMicrogrid.API.Models;
using SmartMicrogrid.API.Tests.Infrastructure;
using Xunit;

namespace SmartMicrogrid.API.Tests;

[Collection("Api")]
public sealed class DashboardTests
{
    private readonly ApiFactory _factory;
    private readonly TestHelpers _helpers;

    // Initializes dashboard tests against the shared disposable API database.
    public DashboardTests(ApiFactory factory)
    {
        _factory = factory;
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

    // Rule: every operator metric is station-scoped, UTC-based, ordered, and excludes past Approved bookings from upcoming.
    [Fact]
    public async Task GetOperatorDashboard_StationScope_ReturnsAllMetricsAndOrderedFutureApproved()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin);
        using var operatorClient = operatorAccount.Client;
        var station = await _helpers.CreateStationAsync(admin);
        var unrelatedStation = await _helpers.CreateStationAsync(admin);
        var now = DateTime.UtcNow;
        var today = now.Date;
        var tomorrow = today.AddDays(1);
        var futureFirst = CreateReservation(station.Id, station.StationName, "Approved", tomorrow.AddHours(1));
        var futureSecond = CreateReservation(station.Id, station.StationName, "Approved", tomorrow.AddHours(2));

        await _factory.Database.GetCollection<EnergyReservation>("EnergyReservation").InsertManyAsync(
        [
            CreateReservation(station.Id, station.StationName, "Pending", today),
            CreateReservation(station.Id, station.StationName, "Pending", tomorrow),
            CreateReservation(station.Id, station.StationName, "Approved", today),
            CreateReservation(station.Id, station.StationName, "Approved", today.AddDays(-1)),
            futureSecond,
            futureFirst,
            CreateReservation(station.Id, station.StationName, "Completed", today.AddDays(-1), now),
            CreateReservation(station.Id, station.StationName, "Completed", today.AddDays(-2), today.AddTicks(-1)),
            CreateReservation(unrelatedStation.Id, unrelatedStation.StationName, "Pending", today),
            CreateReservation(unrelatedStation.Id, unrelatedStation.StationName, "Approved", tomorrow.AddHours(3)),
            CreateReservation(unrelatedStation.Id, unrelatedStation.StationName, "Completed", today.AddDays(-1), now),
        ]);

        var response = await operatorClient.GetAsync($"/api/reports/operator-dashboard?stationId={station.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = await response.Content.ReadFromJsonAsync<OperatorDashboardResponse>();
        Assert.NotNull(dashboard);
        Assert.Equal(1, dashboard.PendingToday);
        Assert.Equal(1, dashboard.ApprovedToday);
        Assert.Equal(1, dashboard.CompletedToday);
        Assert.Equal(2, dashboard.ApprovedFutureCount);
        Assert.Equal(
            new[] { futureFirst.Id, futureSecond.Id },
            dashboard.UpcomingApproved.Select(reservation => reservation.Id));
        Assert.DoesNotContain(dashboard.UpcomingApproved, reservation => reservation.SlotStartTime <= now);
        Assert.All(dashboard.UpcomingApproved, reservation => Assert.Equal(station.Id, reservation.StationId));
    }

    // Rule: omitting stationId returns counts and the first ten upcoming Approved bookings across the entire grid.
    [Fact]
    public async Task GetOperatorDashboard_WithoutStation_ReturnsSystemWideMetrics()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var firstStation = await _helpers.CreateStationAsync(admin);
        var secondStation = await _helpers.CreateStationAsync(admin);
        var now = DateTime.UtcNow;
        var today = now.Date;
        var tomorrow = today.AddDays(1);
        var reservations = _factory.Database.GetCollection<EnergyReservation>("EnergyReservation");

        await reservations.InsertManyAsync(
        [
            CreateReservation(firstStation.Id, firstStation.StationName, "Pending", today),
            CreateReservation(firstStation.Id, firstStation.StationName, "Approved", tomorrow.AddDays(2)),
            CreateReservation(secondStation.Id, secondStation.StationName, "Approved", today),
            CreateReservation(secondStation.Id, secondStation.StationName, "Completed", today.AddDays(-1), now),
        ]);

        var filter = Builders<EnergyReservation>.Filter;
        var pendingTodayFilter = filter.And(
            filter.Eq(reservation => reservation.Status, "Pending"),
            filter.Gte(reservation => reservation.SlotStartTime, today),
            filter.Lt(reservation => reservation.SlotStartTime, tomorrow));
        var approvedTodayFilter = filter.And(
            filter.Eq(reservation => reservation.Status, "Approved"),
            filter.Gte(reservation => reservation.SlotStartTime, today),
            filter.Lt(reservation => reservation.SlotStartTime, tomorrow));
        var completedTodayFilter = filter.And(
            filter.Eq(reservation => reservation.Status, "Completed"),
            filter.Gte(reservation => reservation.CompletedAt, today),
            filter.Lt(reservation => reservation.CompletedAt, tomorrow));
        var futureFilter = filter.And(
            filter.Eq(reservation => reservation.Status, "Approved"),
            filter.Gt(reservation => reservation.SlotStartTime, now));

        var expectedPending = await reservations.CountDocumentsAsync(pendingTodayFilter);
        var expectedApproved = await reservations.CountDocumentsAsync(approvedTodayFilter);
        var expectedCompleted = await reservations.CountDocumentsAsync(completedTodayFilter);
        var expectedFuture = await reservations.CountDocumentsAsync(futureFilter);
        var expectedUpcoming = await reservations.Find(futureFilter)
            .SortBy(reservation => reservation.SlotStartTime)
            .Limit(10)
            .ToListAsync();

        var dashboard = await admin.GetFromJsonAsync<OperatorDashboardResponse>("/api/reports/operator-dashboard");
        Assert.NotNull(dashboard);
        Assert.Equal(expectedPending, dashboard.PendingToday);
        Assert.Equal(expectedApproved, dashboard.ApprovedToday);
        Assert.Equal(expectedCompleted, dashboard.CompletedToday);
        Assert.Equal(expectedFuture, dashboard.ApprovedFutureCount);
        Assert.Equal(
            expectedUpcoming.Select(reservation => reservation.Id),
            dashboard.UpcomingApproved.Select(reservation => reservation.Id));
    }

    // Rule: empty scopes return zero values, role gates remain unchanged, and invalid station filters are rejected.
    [Fact]
    public async Task GetOperatorDashboard_EmptyScopeValidationAndAuthorization_FollowApiConventions()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin);
        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var prosumerClient = prosumer.Client;
        using var anonymousClient = _factory.CreateClient();
        var emptyStation = await _helpers.CreateStationAsync(admin);

        var operatorResponse = await operatorClient.GetAsync($"/api/reports/operator-dashboard?stationId={emptyStation.Id}");
        var backofficeResponse = await admin.GetAsync($"/api/reports/operator-dashboard?stationId={emptyStation.Id}");
        var prosumerResponse = await prosumerClient.GetAsync($"/api/reports/operator-dashboard?stationId={emptyStation.Id}");
        var anonymousResponse = await anonymousClient.GetAsync($"/api/reports/operator-dashboard?stationId={emptyStation.Id}");

        Assert.Equal(HttpStatusCode.OK, operatorResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, backofficeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, prosumerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        var emptyDashboard = await operatorResponse.Content.ReadFromJsonAsync<OperatorDashboardResponse>();
        Assert.NotNull(emptyDashboard);
        Assert.Equal(0, emptyDashboard.PendingToday);
        Assert.Equal(0, emptyDashboard.ApprovedToday);
        Assert.Equal(0, emptyDashboard.CompletedToday);
        Assert.Equal(0, emptyDashboard.ApprovedFutureCount);
        Assert.Empty(emptyDashboard.UpcomingApproved);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await operatorClient.GetAsync("/api/reports/operator-dashboard?stationId=not-an-object-id")).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await operatorClient.GetAsync("/api/reports/operator-dashboard?stationId=000000000000000000000000")).StatusCode);
    }

    // Creates a minimally complete reservation document for dashboard query integration checks.
    private static EnergyReservation CreateReservation(
        string stationId,
        string stationName,
        string status,
        DateTime slotStartTime,
        DateTime? completedAt = null)
    {
        return new EnergyReservation
        {
            Id = ObjectId.GenerateNewId().ToString(),
            ProsumerNic = $"dashboard-{Guid.NewGuid():N}",
            ProsumerName = "Dashboard Test Prosumer",
            StationId = stationId,
            StationName = stationName,
            SlotId = ObjectId.GenerateNewId().ToString(),
            SlotStartTime = slotStartTime,
            SlotEndTime = slotStartTime.AddHours(1),
            CapacityKw = 5,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "dashboard-tests",
            CompletedAt = completedAt,
        };
    }
}
