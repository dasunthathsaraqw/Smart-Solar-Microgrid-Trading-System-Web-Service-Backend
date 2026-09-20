/**
 * File: SlotOverlapException.cs
 * Purpose: Thrown by SlotService when a requested slot's time window overlaps an existing slot
 *          for the same station, so the controller can map it to 409 Conflict specifically.
 * Author: <Your Name>
 * Date: 2026
 */

namespace SmartMicrogrid.API.Services;

public class SlotOverlapException : Exception
{
    public SlotOverlapException(string message) : base(message) { }
}
