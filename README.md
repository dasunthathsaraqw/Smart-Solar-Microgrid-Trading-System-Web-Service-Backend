# Smart Solar Microgrid Web Service Backend

ASP.NET Core 10 controller API backed by MongoDB. JWT roles are Backoffice, GridOperator and Prosumer. Browser clients use configured CORS origins; native Android clients do not use CORS.

## Local setup

Start MongoDB, then from this directory:

```powershell
dotnet user-secrets set "Jwt:Key" "REPLACE_WITH_A_PRIVATE_RANDOM_KEY_AT_LEAST_32_CHARACTERS"
dotnet user-secrets set "MongoDB:ConnectionString" "mongodb://localhost:27017/"
dotnet run
```

Replace the example JWT value with your own random secret. The committed `appsettings.json` deliberately retains a public placeholder and localhost MongoDB URL; startup refuses the placeholder or any key shorter than 32 characters. User secrets are for local Development only. Swagger UI is at `/swagger` in Development. `Hosting:UseHttpsRedirection` defaults to false so Android devices can call an HTTP LAN binding without an untrusted-certificate redirect. Set it true only after configuring working TLS. Plain HTTP exposes passwords and tokens to others on the network; use trusted HTTPS outside a controlled demo LAN.

Optional demo data: set `Seeding__SeedSampleData=true` for the API process (or `Seeding:SeedSampleData` in local configuration). The seeder runs only when `SolarStationInfo` is empty. It adds seven stations (six active), 127 slots, five prosumers and four reservations. The normal admin seeder runs when Users is empty. Use a disposable MongoDB database when trying sample data.

| Account | Email | Password | State |
|---|---|---|---|
| Backoffice admin | `admin@smartsolar.com` | `Admin@123` | Active, seeded on empty Users |
| Grid operator | `operator@smartsolar.com` | `Operator@123` | Active, sample data only |
| Prosumer 1 | `prosumer1@smartsolar.com` | `Prosumer@123` | Active, sample data only |
| Prosumer 2 | `prosumer2@smartsolar.com` | `Prosumer@123` | Active, sample data only |
| Prosumer 3 | `prosumer3@smartsolar.com` | `Prosumer@123` | Active, sample data only |
| Prosumer 4 | `prosumer4@smartsolar.com` | `Prosumer@123` | Pending approval; login returns 403 |
| Prosumer 5 | `prosumer5@smartsolar.com` | `Prosumer@123` | Deactivated/requested; login returns 403 |

These are public demo credentials: never enable sample seeding against a production database. See [DEPLOYMENT.md](DEPLOYMENT.md) for IIS setup, secrets, LAN bindings and troubleshooting.

## Running the tests

Prerequisite: A local MongoDB instance must be running (e.g. `mongodb://localhost:27017/`). Integration tests automatically create and clean up isolated test databases (`SmartSolarTests_*`).

To execute the entire integration test suite from the solution root:

```powershell
dotnet test SmartMicrogrid.slnx
```

### Test Suite Summary

- **PureRuleTests**: Proves pure unit logic including booking window boundary rules, twelve-hour notice rules, and Haversine distance calculation.
- **StationManagementTests**: Proves Backoffice creation, updates, and activation/deactivation controls for solar charging stations.
- **NearbyStationTests**: Proves location-based search and distance filtering for active charging stations.
- **SlotAvailabilityTests**: Proves slot creation, status queries, and seven-day bookable slot availability logic.
- **ProsumerAccountTests**: Proves prosumer registration, activation lifecycle, JWT NIC claims, profile self-service, password changes, and deactivation workflows.
- **ReservationRuleTests**: Proves booking windows, notice rules, ownership isolation, body NIC forgery protection, and QR token lifecycles.
- **OperatorFlowTests**: Proves GridOperator QR token scanning, atomic completion, slot freeing, replay prevention, station validation, concurrency protection, and role-based access control.
- **DashboardTests**: Proves owner-scoped prosumer dashboard metrics and station-scoped GridOperator activity counters.

## Endpoints

`BO` = Backoffice, `GO` = GridOperator, `P` = Prosumer. All routes are relative to the API host. Query parameters are optional unless specified by a request model.

| Method | Route | Roles | Purpose |
|---|---|---|---|
| POST | `/api/auth/login` | Anonymous | Validate credentials and issue JWT |
| GET | `/api/auth/me` | Any authenticated | Return signed-in identity |
| GET | `/api/health` | Anonymous | Ping MongoDB; 200 or 503 |
| GET | `/api/users` | BO | List users, optionally by role/status |
| GET | `/api/users/{id}` | BO | Get user |
| POST | `/api/users` | BO | Create Backoffice or GridOperator |
| PUT | `/api/users/{id}` | BO | Update user |
| PUT | `/api/users/{id}/deactivate` | BO | Deactivate user |
| PUT | `/api/users/{id}/reactivate` | BO | Reactivate user |
| GET | `/api/prosumers` | BO | List prosumers by optional status |
| GET | `/api/prosumers/pending` | BO | List awaiting approval |
| GET | `/api/prosumers/pending-deactivations` | BO | List deactivation requests |
| GET | `/api/prosumers/{nic}` | BO | Get prosumer by NIC |
| POST | `/api/prosumers` | BO | Create pending prosumer |
| PUT | `/api/prosumers/{nic}` | BO | Update prosumer |
| PUT | `/api/prosumers/{nic}/deactivate` | BO | Deactivate prosumer |
| PUT | `/api/prosumers/{nic}/reactivate` | BO | Approve or reactivate prosumer |
| POST | `/api/prosumers/register` | Anonymous | Self-register pending prosumer and user |
| GET | `/api/prosumers/me` | P | Get own profile |
| PUT | `/api/prosumers/me` | P | Update own profile |
| PUT | `/api/prosumers/me/password` | P | Change own password |
| PUT | `/api/prosumers/me/request-deactivation` | P | Request own deactivation |
| GET | `/api/stations` | BO, GO, P | List stations; non-BO sees active only |
| GET | `/api/stations/{id}` | BO, GO, P | Get station; non-BO cannot see inactive |
| GET | `/api/stations/nearby` | BO, GO, P | Nearby active stations with distance and slot count |
| POST | `/api/stations` | BO | Create station |
| PUT | `/api/stations/{id}` | BO | Update station |
| PUT | `/api/stations/{id}/deactivate` | BO | Deactivate station |
| PUT | `/api/stations/{id}/reactivate` | BO | Reactivate station |
| GET | `/api/slots` | BO, GO | List slots with optional filters |
| GET | `/api/slots/{id}` | BO, GO | Get slot |
| GET | `/api/slots/station/{stationId}` | BO, GO | List station slots |
| GET | `/api/slots/station/{stationId}/available` | BO, GO, P | List bookable slots in seven-day window |
| POST | `/api/slots` | BO, GO | Create slot |
| POST | `/api/slots/bulk` | BO, GO | Create a day's slots in bulk |
| PUT | `/api/slots/{id}` | BO, GO | Update unbooked slot |
| DELETE | `/api/slots/{id}` | BO, GO | Delete unbooked slot |
| GET | `/api/reservations` | BO, GO | List reservations with filters |
| POST | `/api/reservations/search` | BO, GO | Search/paginate reservations |
| GET | `/api/reservations/{id}` | BO, GO | Get reservation |
| POST | `/api/reservations` | BO, GO | Book a slot for a prosumer |
| PUT | `/api/reservations/{id}` | BO, GO | Move pending reservation |
| PUT | `/api/reservations/{id}/cancel` | BO, GO | Cancel reservation; BO can override notice rule |
| PUT | `/api/reservations/{id}/approve` | BO, GO | Approve and issue QR token |
| PUT | `/api/reservations/{id}/complete` | BO, GO | Complete and free slot |
| GET | `/api/reservations/{id}/qr` | BO, GO | Get approved reservation QR token |
| POST | `/api/reservations/verify-qr` | GO | Validate QR token at a station |
| POST | `/api/reservations/scan-complete` | GO | Validate QR and complete atomically |
| GET | `/api/reservations/my` | P | List own reservations |
| POST | `/api/reservations/my/search` | P | Search own reservations |
| GET | `/api/reservations/my/{id}` | P | Get owned reservation |
| POST | `/api/reservations/my` | P | Book own slot |
| PUT | `/api/reservations/my/{id}` | P | Move own pending reservation |
| PUT | `/api/reservations/my/{id}/cancel` | P | Cancel own reservation |
| GET | `/api/reservations/my/{id}/qr` | P | Get own approved QR token |
| GET | `/api/reports/dashboard-summary` | BO, GO | Dashboard KPI summary |
| GET | `/api/reports/reservations-by-status` | BO, GO | Reservation status chart |
| GET | `/api/reports/reservations-per-day` | BO, GO | Daily reservation counts |
| GET | `/api/reports/top-stations` | BO, GO | Top stations chart |
| GET | `/api/reports/energy-traded` | BO, GO | Traded-energy chart |
| GET | `/api/reports/recent-bookings` | BO, GO | Recent bookings |
| GET | `/api/reports/pending-approvals` | BO, GO | Approval queue |
| GET | `/api/reports/operator-dashboard` | BO, GO | Live operator dashboard |
| GET | `/api/reports/my-dashboard` | P | Live own-prosumer dashboard |
