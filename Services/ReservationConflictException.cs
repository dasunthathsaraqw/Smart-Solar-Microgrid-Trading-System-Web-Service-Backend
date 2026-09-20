/**
 * File: ReservationConflictException.cs
 * Purpose: Thrown by ReservationService when a requested slot is already booked, so the
 *          controller can map it to 409 Conflict specifically.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

namespace SmartMicrogrid.API.Services;

public class ReservationConflictException : Exception
{
    public ReservationConflictException(string message) : base(message) { }
}
