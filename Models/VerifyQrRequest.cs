/**
 * File: VerifyQrRequest.cs
 * Purpose: Request payload for POST /api/reservations/verify-qr — used by a Grid Operator's
 *          station terminal to validate a prosumer's QR code at the point of service.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class VerifyQrRequest
{
    /// <summary>Opaque token read from the Approved reservation QR code.</summary>
    [Required]
    public string QrToken { get; set; } = string.Empty;

    /// <summary>Station ObjectId where the QR is presented; must match the operator's persisted assignment.</summary>
    [Required]
    public string StationId { get; set; } = string.Empty;
}
