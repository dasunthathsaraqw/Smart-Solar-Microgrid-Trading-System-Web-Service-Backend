/**
 * File: SlotOverlapException.cs
 * Purpose: Thrown by SlotService when a requested slot's time window overlaps an existing slot
 *          for the same station, so the controller can map it to 409 Conflict specifically.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

namespace SmartMicrogrid.API.Services;

public class SlotOverlapException : Exception
{
    // Creates an exception for overlapping station slots.
    public SlotOverlapException(string message) : base(message) { }
}
