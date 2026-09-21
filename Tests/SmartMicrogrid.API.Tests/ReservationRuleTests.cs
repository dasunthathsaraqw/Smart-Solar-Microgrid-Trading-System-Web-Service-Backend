/**
 * File: ReservationRuleTests.cs
 * Purpose: Integration checks for booking windows, notice rules, ownership and QR lifecycle.
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
public sealed class ReservationRuleTests
{
    private readonly TestHelpers _helpers;

    // Initializes reservation tests against the shared disposable API database.
    public ReservationRuleTests(ApiFactory factory)
    {
        _helpers = new TestHelpers(factory);
    }

    // Rule: bookings beyond seven days are rejected while a six-day booking is accepted.
    [Fact]
    public async Task Book_SixVersusEightDays_EnforcesSevenDayWindow()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var account = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var client = account.Client;
        var station = await _helpers.CreateStationAsync(admin);
        var tooLate = await _helpers.CreateSlotAsync(admin, station.Id, 8 * 24);
        var allowed = await _helpers.CreateSlotAsync(admin, station.Id, 6 * 24);
        var rejected = await client.PostAsJsonAsync("/api/reservations/my", new { stationId = station.Id, slotId = tooLate.Id });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var booking = await _helpers.BookAsync(client, station.Id, allowed.Id);
        Assert.Equal("Pending", booking.Reservation.Status);
    }

    // Rule: moving a pending booking needs at least twelve hours' notice on the original slot.
    [Fact]
    public async Task Update_ThirteenVersusElevenHours_EnforcesNotice()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var account = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var client = account.Client;
        var station = await _helpers.CreateStationAsync(admin);
        var thirteenHours = await _helpers.CreateSlotAsync(admin, station.Id, 13);
        var elevenHours = await _helpers.CreateSlotAsync(admin, station.Id, 11);
        var destinationA = await _helpers.CreateSlotAsync(admin, station.Id, 30);
        var destinationB = await _helpers.CreateSlotAsync(admin, station.Id, 40);
        var movable = await _helpers.BookAsync(client, station.Id, thirteenHours.Id);
        var locked = await _helpers.BookAsync(client, station.Id, elevenHours.Id);
        var accepted = await client.PutAsJsonAsync($"/api/reservations/my/{movable.Reservation.Id}", new { newSlotId = destinationA.Id });
        var rejected = await client.PutAsJsonAsync($"/api/reservations/my/{locked.Reservation.Id}", new { newSlotId = destinationB.Id });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    // Rule: a Prosumer cannot cancel within twelve hours, but Backoffice may override that limit.
    [Fact]
    public async Task Cancel_ElevenHours_RequiresBackofficeOverride()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var account = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var client = account.Client;
        var station = await _helpers.CreateStationAsync(admin);
        var slot = await _helpers.CreateSlotAsync(admin, station.Id, 11);
        var booking = await _helpers.BookAsync(client, station.Id, slot.Id);
        var denied = await client.PutAsJsonAsync($"/api/reservations/my/{booking.Reservation.Id}/cancel", new { reason = "Changed plans" });
        var overridden = await admin.PutAsJsonAsync($"/api/reservations/{booking.Reservation.Id}/cancel", new { reason = "Operator override" });
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        Assert.Equal(HttpStatusCode.OK, overridden.StatusCode);
        Assert.Equal("Cancelled", (await overridden.Content.ReadFromJsonAsync<ReservationResponse>())!.Status);
    }

    // Rule: a forged body NIC cannot book a slot for anyone other than the signed-in Prosumer.
    [Fact]
    public async Task Book_ForgedBodyNic_UsesTokenOwner()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var owner = await _helpers.RegisterAndApproveProsumerAsync(admin);
        var victim = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var ownerClient = owner.Client;
        using var victimClient = victim.Client;
        var station = await _helpers.CreateStationAsync(admin);
        var slot = await _helpers.CreateSlotAsync(admin, station.Id, 30);
        var booking = await _helpers.BookAsync(ownerClient, station.Id, slot.Id, victim.Nic);
        Assert.Equal(owner.Nic, booking.Reservation.ProsumerNic);
        Assert.Empty((await victimClient.GetFromJsonAsync<List<ReservationResponse>>("/api/reservations/my"))!);
    }

    // Rule: a different Prosumer sees 404 for another owner's read, update, cancel and QR routes.
    [Fact]
    public async Task OwnReservation_AnotherProsumer_ReturnsNotFoundForEveryRoute()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var owner = await _helpers.RegisterAndApproveProsumerAsync(admin);
        var stranger = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var ownerClient = owner.Client;
        using var strangerClient = stranger.Client;
        var station = await _helpers.CreateStationAsync(admin);
        var slot = await _helpers.CreateSlotAsync(admin, station.Id, 30);
        var newSlot = await _helpers.CreateSlotAsync(admin, station.Id, 40);
        var booking = await _helpers.BookAsync(ownerClient, station.Id, slot.Id);
        var id = booking.Reservation.Id;
        var responses = new[]
        {
            await strangerClient.GetAsync($"/api/reservations/my/{id}"),
            await strangerClient.PutAsJsonAsync($"/api/reservations/my/{id}", new { newSlotId = newSlot.Id }),
            await strangerClient.PutAsJsonAsync($"/api/reservations/my/{id}/cancel", new { reason = "Not mine" }),
            await strangerClient.GetAsync($"/api/reservations/my/{id}/qr"),
        };
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
    }

    // Rule: QR tokens are unavailable while Pending and become available only after approval.
    [Fact]
    public async Task GetQr_PendingThenApproved_FollowsLifecycle()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var account = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var client = account.Client;
        var station = await _helpers.CreateStationAsync(admin);
        var slot = await _helpers.CreateSlotAsync(admin, station.Id, 30);
        var booking = await _helpers.BookAsync(client, station.Id, slot.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/reservations/my/{booking.Reservation.Id}/qr")).StatusCode);
        await _helpers.ApproveAsync(admin, booking.Reservation.Id);
        var qr = await client.GetAsync($"/api/reservations/my/{booking.Reservation.Id}/qr");
        Assert.Equal(HttpStatusCode.OK, qr.StatusCode);
        Assert.Contains("qrToken", await qr.Content.ReadAsStringAsync());
    }

    // Rule: create, update and cancel confirmations report the final status, message, time and editability.
    [Fact]
    public async Task ReservationAction_CreateUpdateCancel_ReturnsComputedConfirmationFields()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var account = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var client = account.Client;
        var station = await _helpers.CreateStationAsync(admin);
        var first = await _helpers.CreateSlotAsync(admin, station.Id, 30);
        var second = await _helpers.CreateSlotAsync(admin, station.Id, 40);
        var created = await _helpers.BookAsync(client, station.Id, first.Id);
        Assert.Equal("Created", created.Action);
        Assert.Equal("Reservation created successfully.", created.Message);
        Assert.Equal("Pending", created.Reservation.Status);
        Assert.True(created.CanStillModify);
        Assert.InRange(created.HoursUntilSlot, 29.8, 30.1);

        var updateResponse = await client.PutAsJsonAsync($"/api/reservations/my/{created.Reservation.Id}", new { newSlotId = second.Id });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ReservationActionResponse>();
        Assert.Equal("Updated", updated!.Action);
        Assert.Equal(second.Id, updated.Reservation.SlotId);
        Assert.True(updated.CanStillModify);
        Assert.InRange(updated.HoursUntilSlot, 39.8, 40.1);

        var cancelResponse = await client.PutAsJsonAsync($"/api/reservations/my/{created.Reservation.Id}/cancel", new { reason = "Test" });
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelled = await cancelResponse.Content.ReadFromJsonAsync<ReservationActionResponse>();
        Assert.Equal("Cancelled", cancelled!.Action);
        Assert.Equal("Reservation cancelled successfully.", cancelled.Message);
        Assert.Equal("Cancelled", cancelled.Reservation.Status);
        Assert.False(cancelled.CanStillModify);
        Assert.InRange(cancelled.HoursUntilSlot, 39.8, 40.1);
    }
}
