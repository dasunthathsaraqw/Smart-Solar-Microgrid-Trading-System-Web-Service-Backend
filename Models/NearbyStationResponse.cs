/**
 * File: NearbyStationResponse.cs
 * Purpose: Station search result with distance and seven-day bookable-slot availability.
 * Author: AUTHOR_NAME AUTHOR_IT_NUMBER
 * Date: 2026
 */

namespace SmartMicrogrid.API.Models;

public class NearbyStationResponse : StationResponse
{
    public double DistanceKm { get; set; }
    public int AvailableSlotCount { get; set; }
}
