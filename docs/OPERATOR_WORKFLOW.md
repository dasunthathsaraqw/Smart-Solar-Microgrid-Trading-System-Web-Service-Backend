# Grid Operator API contract

This document is the backend contract for the Grid Operator energy-transfer workflow. Routes are relative to the API host, JSON uses camelCase, and authenticated requests use:

```http
Authorization: Bearer <token>
```

Timestamps are ISO 8601 values. Operational day counters use UTC.

## Endpoint summary

| Method | Route | Allowed role | Success | Documented errors |
|---|---|---|---|---|
| `POST` | `/api/auth/login` | Anonymous | `200 LoginResponse` | `400`, `401`, `403` |
| `GET` | `/api/auth/me` | Authenticated | `200` current identity | `401` |
| `GET` | `/api/reports/operator-dashboard` | GridOperator, Backoffice | `200 OperatorDashboardResponse` | `400`, `401`, `403` |
| `GET` | `/api/reservations/operator/history` | GridOperator | `200 PagedResult<ReservationResponse>` | `400`, `401`, `403` |
| `GET` | `/api/reservations/{id}/qr` | GridOperator, Backoffice | `200 { qrToken }` | `400`, `401`, `403`, `404` |
| `POST` | `/api/reservations/verify-qr` | GridOperator | `200 ReservationResponse` | `400`, `401`, `403` |
| `POST` | `/api/reservations/scan-complete` | GridOperator | `200 ReservationResponse` | `400`, `401`, `403` |
| `PUT` | `/api/reservations/{id}/complete` | Backoffice direct completion | `200 ReservationResponse` | `400`, `401`, `403`, `404` |

None of these endpoints currently returns `409 Conflict`.

## Authentication and station context

### Login

`POST /api/auth/login`

```json
{
  "email": "operator@smartsolar.com",
  "password": "Operator@123"
}
```

Assigned GridOperator response:

```json
{
  "token": "eyJhbGciOi...",
  "name": "Grid Operator",
  "email": "operator@smartsolar.com",
  "role": "GridOperator",
  "stationId": "66f12ab34cd56ef789012345",
  "expiresAt": "2026-09-22T12:00:00Z"
}
```

An unassigned GridOperator receives `stationId: null`. Invalid credentials return `401`; pending, inactive, or deactivated accounts return `403`.

### Current user

`GET /api/auth/me`

```json
{
  "id": "66f100000000000000000001",
  "name": "Grid Operator",
  "email": "operator@smartsolar.com",
  "role": "GridOperator",
  "stationId": "66f12ab34cd56ef789012345"
}
```

The API obtains the user ID from the signed JWT `NameIdentifier` claim, then reloads the current User document. `stationId` comes from persisted server-side data, not request input and not a station claim inside the JWT. Assignment changes are therefore visible through `/api/auth/me` without issuing a new token. An unassigned operator receives `stationId: null`.

### Station authorization rules

For station-scoped GridOperator endpoints:

- Omitting an optional `stationId` uses the persisted assignment.
- Supplying the matching `stationId` is accepted.
- Supplying another station returns `403`.
- Having no assignment returns `403`.
- Backoffice behavior is not restricted by GridOperator station assignment.

## Operator dashboard

`GET /api/reports/operator-dashboard`

Optional query parameter:

| Parameter | Meaning |
|---|---|
| `stationId` | Station ObjectId. GridOperators may omit it or send only their persisted assignment. Backoffice may omit it for system-wide data. |

An invalid or nonexistent resolved station returns `400`. A foreign station or missing GridOperator assignment returns `403`.

`OperatorDashboardResponse` fields:

| Field | Meaning |
|---|---|
| `pendingToday` | Pending reservations whose slot starts during the current UTC day. |
| `approvedToday` | Approved reservations whose slot starts during the current UTC day. |
| `completedToday` | Reservations whose `completedAt` is during the current UTC day. |
| `approvedFutureCount` | All Approved reservations with a slot start later than the current UTC instant. |
| `upcomingApproved` | The next ten future Approved reservations, ordered by `slotStartTime`; each item is a `ReservationResponse`. |

```json
{
  "pendingToday": 2,
  "approvedToday": 1,
  "completedToday": 4,
  "approvedFutureCount": 3,
  "upcomingApproved": []
}
```

## Completed transaction history

`GET /api/reservations/operator/history`

| Parameter | Default | Meaning |
|---|---:|---|
| `stationId` | assigned station | Optional matching station ObjectId. |
| `dateFrom` | none | Inclusive lower bound on `completedAt`. |
| `dateTo` | none | Inclusive upper bound on `completedAt`. |
| `page` | `1` | One-based page number. |
| `pageSize` | `10` | Items per page, from 1 through 100. |

Only Completed reservations are returned. Results are restricted to the persisted assignment and ordered by `completedAt` newest first. Invalid pagination or a reversed date range returns `400`; foreign or missing station assignment returns `403`.

The response uses these `PagedResult` fields:

| Field | Meaning |
|---|---|
| `items` | `ReservationResponse` objects in the requested page. |
| `totalCount` | Total matching completed reservations. |
| `page` | Current one-based page. |
| `pageSize` | Requested page size. |
| `totalPages` | Total pages, or zero when no rows match. |
| `hasNextPage` | Whether a later page exists. |
| `hasPreviousPage` | Whether an earlier page exists. |

```json
{
  "items": [
    {
      "id": "66f200000000000000000001",
      "prosumerNic": "200012345678",
      "prosumerName": "Sample Prosumer",
      "stationId": "66f12ab34cd56ef789012345",
      "stationName": "Colombo Solar Hub",
      "slotId": "66f300000000000000000001",
      "slotStartTime": "2026-09-22T09:00:00Z",
      "slotEndTime": "2026-09-22T10:00:00Z",
      "capacityKw": 5.0,
      "status": "Completed",
      "qrGeneratedAt": "2026-09-21T08:00:00Z",
      "createdAt": "2026-09-20T07:00:00Z",
      "createdBy": "prosumer@example.com",
      "updatedAt": "2026-09-22T09:10:00Z",
      "approvedAt": "2026-09-21T08:00:00Z",
      "approvedBy": "admin@smartsolar.com",
      "completedAt": "2026-09-22T09:10:00Z",
      "completedBy": "operator@smartsolar.com",
      "cancelledAt": null,
      "cancelledBy": null,
      "cancellationReason": null
    }
  ],
  "totalCount": 1,
  "page": 1,
  "pageSize": 10,
  "totalPages": 1,
  "hasNextPage": false,
  "hasPreviousPage": false
}
```

## Required QR transfer workflow

1. The operator logs in with `POST /api/auth/login`.
2. The client obtains the current persisted `stationId` from the login response or `GET /api/auth/me`.
3. The operator views assigned-station workload through `GET /api/reports/operator-dashboard` and reservation endpoints.
4. The Prosumer presents the QR for an Approved reservation.
5. The client sends `qrToken` and `stationId` to `POST /api/reservations/verify-qr`.
6. The server checks the GridOperator role, persisted station assignment, token, Approved status, reservation station, and the +/-24-hour slot-start window.
7. The client displays the returned verified `ReservationResponse`.
8. The operator confirms that the physical energy transfer should be finalized.
9. The client sends the same `qrToken` and `stationId` to `POST /api/reservations/scan-complete`.
10. The server atomically changes the reservation from Approved to Completed.
11. `completedAt` and `completedBy` are saved.
12. The QR token is cleared and becomes unusable.
13. The booking slot is released.
14. The completed transfer appears in operator history and dashboard totals.

GridOperators must **not** call `PUT /api/reservations/{id}/complete`. That endpoint is retained as a Backoffice administrative/recovery path and always returns `403` to GridOperators.

### Verify request

`POST /api/reservations/verify-qr`

```json
{
  "qrToken": "0a1b2c3d4e5f...",
  "stationId": "66f12ab34cd56ef789012345"
}
```

Successful verification returns the reservation without mutating it:

```json
{
  "id": "66f200000000000000000001",
  "prosumerNic": "200012345678",
  "prosumerName": "Sample Prosumer",
  "stationId": "66f12ab34cd56ef789012345",
  "stationName": "Colombo Solar Hub",
  "slotId": "66f300000000000000000001",
  "slotStartTime": "2026-09-22T09:00:00Z",
  "slotEndTime": "2026-09-22T10:00:00Z",
  "capacityKw": 5.0,
  "status": "Approved",
  "qrGeneratedAt": "2026-09-21T08:00:00Z",
  "createdAt": "2026-09-20T07:00:00Z",
  "createdBy": "prosumer@example.com",
  "updatedAt": "2026-09-21T08:00:00Z",
  "approvedAt": "2026-09-21T08:00:00Z",
  "approvedBy": "admin@smartsolar.com",
  "completedAt": null,
  "completedBy": null,
  "cancelledAt": null,
  "cancelledBy": null,
  "cancellationReason": null
}
```

### Scan-complete request and response

`POST /api/reservations/scan-complete`

```json
{
  "qrToken": "0a1b2c3d4e5f...",
  "stationId": "66f12ab34cd56ef789012345"
}
```

Successful completion:

```json
{
  "id": "66f200000000000000000001",
  "prosumerNic": "200012345678",
  "prosumerName": "Sample Prosumer",
  "stationId": "66f12ab34cd56ef789012345",
  "stationName": "Colombo Solar Hub",
  "slotId": "66f300000000000000000001",
  "slotStartTime": "2026-09-22T09:00:00Z",
  "slotEndTime": "2026-09-22T10:00:00Z",
  "capacityKw": 5.0,
  "status": "Completed",
  "qrGeneratedAt": "2026-09-21T08:00:00Z",
  "createdAt": "2026-09-20T07:00:00Z",
  "createdBy": "prosumer@example.com",
  "updatedAt": "2026-09-22T09:10:00Z",
  "approvedAt": "2026-09-21T08:00:00Z",
  "approvedBy": "admin@smartsolar.com",
  "completedAt": "2026-09-22T09:10:00Z",
  "completedBy": "operator@smartsolar.com",
  "cancelledAt": null,
  "cancelledBy": null,
  "cancellationReason": null
}
```

The raw QR token is intentionally not part of `ReservationResponse`. While a reservation is Approved, authorized callers retrieve it separately from `GET /api/reservations/{id}/qr`, which returns:

```json
{
  "qrToken": "0a1b2c3d4e5f..."
}
```

## Reservation response fields

| Field | Meaning |
|---|---|
| `id` | Reservation ObjectId. |
| `prosumerNic` | Owning Prosumer NIC. |
| `prosumerName` | Prosumer name captured for the reservation. |
| `stationId` | Reservation station ObjectId. |
| `stationName` | Station name captured for the reservation. |
| `slotId` | Reserved slot ObjectId. |
| `slotStartTime` | UTC slot start. |
| `slotEndTime` | UTC slot end. |
| `capacityKw` | Reserved energy capacity in kilowatts. |
| `status` | `Pending`, `Approved`, `Completed`, or `Cancelled`. |
| `qrGeneratedAt` | UTC time the QR was generated; null before approval. |
| `createdAt` | UTC creation time. |
| `createdBy` | Identity recorded as creator. |
| `updatedAt` | UTC time of the latest lifecycle update, when available. |
| `approvedAt` | UTC approval time, or null. |
| `approvedBy` | Approving identity, or null. |
| `completedAt` | UTC completion time, or null. |
| `completedBy` | Completing operator/administrator identity, or null. |
| `cancelledAt` | UTC cancellation time, or null. |
| `cancelledBy` | Cancelling identity, or null. |
| `cancellationReason` | Optional cancellation reason. |

`ReservationActionResponse` is used by Prosumer self-service create/update/cancel routes, not by the operator QR endpoints. Its existing fields are `action`, `reservation`, `message`, `hoursUntilSlot`, and `canStillModify`.

## Important error responses

Unassigned GridOperator:

```http
403 Forbidden
```

```json
{
  "error": "Grid Operator is not assigned to a station."
}
```

Foreign requested station:

```http
403 Forbidden
```

```json
{
  "error": "Grid Operator is not assigned to the requested station."
}
```

GridOperator direct completion:

```http
403 Forbidden
```

```json
{
  "error": "Grid Operators must complete energy transfers through QR verification."
}
```

QR validation and scan failures use `400` with one of the current messages, including:

- `QR code not recognized`
- `QR is no longer valid`
- `QR does not belong to this station`
- `QR is outside the valid time window`
- `This reservation has already been completed`

The persisted-assignment check occurs before QR validation. Therefore a request `stationId` that differs from the signed operator's assignment returns the foreign-station `403`, not a QR-specific `400`.
