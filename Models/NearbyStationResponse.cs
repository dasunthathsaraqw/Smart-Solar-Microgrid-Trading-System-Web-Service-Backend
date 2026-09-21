/**
 * File: NearbyStationResponse.cs
 * Purpose: Station search result with distance and seven-day bookable-slot availability.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

namespace SmartMicrogrid.API.Models;

public class NearbyStationResponse : StationResponse
{
    public double DistanceKm { get; set; }
    public int AvailableSlotCount { get; set; }
}
