/**
 * File: NearbyStationResponse.cs
 * Purpose: Station search result with distance and seven-day bookable-slot availability.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

namespace SmartMicrogrid.API.Models;

public class NearbyStationResponse : StationResponse
{
    // Straight-line (great-circle) distance from the caller's coordinates, rounded to 2 decimals.
    public double DistanceKm { get; set; }
    // Live count of unbooked slots starting within the next 7 days. Distinct from the inherited AvailableSlots, which is a stored figure.
    public int AvailableSlotCount { get; set; }
}
