/**
 * File: PureRuleTests.cs
 * Purpose: Deterministic distance and reservation-time boundary tests without MongoDB.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using SmartMicrogrid.API.Models;
using SmartMicrogrid.API.Services;
using Xunit;

namespace SmartMicrogrid.API.Tests;

public sealed class PureRuleTests
{
    // Rule: haversine distance matches known Sri Lankan city-area pairs within one kilometre.
    [Theory]
    [InlineData(6.9271, 79.8612, 7.2906, 80.6337, 94)]
    [InlineData(6.9271, 79.8612, 5.9500, 80.2168, 116)]
    public void CalculateDistanceKm_CityPair_ReturnsExpectedDistance(
        double originLatitude, double originLongitude, double destinationLatitude, double destinationLongitude, double expectedKm)
    {
        var actual = StationService.CalculateDistanceKm(originLatitude, originLongitude, destinationLatitude, destinationLongitude);
        Assert.InRange(actual, expectedKm - 1, expectedKm + 1);
    }

    // Rule: distance from any point to itself is zero.
    [Fact]
    public void CalculateDistanceKm_SamePoint_ReturnsZero()
    {
        Assert.Equal(0, StationService.CalculateDistanceKm(6.9271, 79.8612, 6.9271, 79.8612));
    }

    // Rule: travelling from A to B has the same great-circle distance as B to A.
    [Fact]
    public void CalculateDistanceKm_ReversedPoints_ReturnsSameDistance()
    {
        var forward = StationService.CalculateDistanceKm(6.9271, 79.8612, 7.2906, 80.6337);
        var reverse = StationService.CalculateDistanceKm(7.2906, 80.6337, 6.9271, 79.8612);
        Assert.Equal(forward, reverse, 9);
    }

    // Rule: the seven-day booking limit includes the boundary but excludes later and past slots.
    [Fact]
    public void IsWithinBookingWindow_SevenDayBoundary_UsesInclusiveUpperLimit()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(ReservationService.IsWithinBookingWindow(now.AddDays(7), now));
        Assert.False(ReservationService.IsWithinBookingWindow(now.AddDays(7).AddTicks(1), now));
        Assert.False(ReservationService.IsWithinBookingWindow(now, now));
    }

    // Rule: twelve hours of notice allows modification, while one tick less does not.
    [Fact]
    public void HasTwelveHoursNotice_ExactBoundary_AllowsOnlySufficientNotice()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(ReservationService.HasTwelveHoursNotice(now.AddHours(12), now));
        Assert.False(ReservationService.HasTwelveHoursNotice(now.AddHours(12).AddTicks(-1), now));
        Assert.Equal(12, ReservationService.HoursUntilSlot(now.AddHours(12), now));
    }

    // Rule: the mobile modification flag requires Pending status and at least twelve hours left.
    [Fact]
    public void CanStillModify_StatusAndNotice_ReturnsCorrectFlag()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var reservation = new ReservationResponse { Status = "Pending", SlotStartTime = now.AddHours(12) };
        Assert.True(ReservationService.CanStillModify(reservation, now));
        reservation.SlotStartTime = reservation.SlotStartTime.AddTicks(-1);
        Assert.False(ReservationService.CanStillModify(reservation, now));
        reservation.SlotStartTime = now.AddHours(13);
        reservation.Status = "Approved";
        Assert.False(ReservationService.CanStillModify(reservation, now));
    }
}
