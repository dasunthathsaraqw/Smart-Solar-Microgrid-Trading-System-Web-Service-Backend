# Smart Solar Microgrid — Web Service Backend (Stage 1)

ASP.NET Core 10 Web API (controller-based) providing role-based authentication and
Backoffice-only user management, backed by MongoDB Atlas.

## Endpoints

| Method | Route              | Auth                    | Purpose                          |
|--------|--------------------|--------------------------|-----------------------------------|
| POST   | `/api/auth/login`  | none                     | Login, returns JWT + user info    |
| GET    | `/api/auth/me`     | any authenticated user   | Returns current user from claims  |
| POST   | `/api/users`       | Backoffice only          | Create a Backoffice/GridOperator user |
| GET    | `/api/users`       | Backoffice only          | List all users                    |
| GET    | `/api/users/{id}`  | Backoffice only          | Get a single user                 |
| PUT    | `/api/users/{id}`  | Backoffice only          | Update a user                     |
| DELETE | `/api/users/{id}`  | Backoffice only          | Soft-delete (sets `isActive=false`) |

## Setup

```bash
dotnet restore
dotnet run
```

Swagger UI is available at `/swagger` in the Development environment.
On first run, HTTPS uses ASP.NET Core's local dev certificate — trust it once with:

```bash
dotnet dev-certs https --trust
```

## Configuration

`appsettings.json` contains:

- `MongoDB:ConnectionString` / `MongoDB:DatabaseName` — MongoDB Atlas connection.
- `Jwt:Key` / `Jwt:Issuer` / `Jwt:Audience` / `Jwt:ExpiryMinutes` — JWT signing settings.

**Note:** the `Jwt:Key` here is a locally-generated development secret, and the MongoDB
connection string is the one supplied for this assignment. Replace both before any
real/shared deployment — do not commit production credentials to a public repo.

## Seed data

On first startup, if the `Users` collection is empty, a default Backoffice account is created:

- Email: `admin@smartsolar.com`
- Password: `Admin@123`

## Known issue: MongoDB Atlas hostname does not resolve

While testing this stage, the three shard hostnames in the provided connection string
(`ac-lr5kmzv-shard-00-0{0,1,2}.i42ot8y.mongodb.net`) returned **NXDOMAIN** (non-existent
domain) on DNS lookup — not a firewall/timeout, but "this name does not exist." This means
the app cannot currently reach MongoDB Atlas, so the seed step and any endpoint touching
the database will fail with a `MongoDB.Driver.MongoConnectionException` /
`TimeoutException` until a valid connection string is supplied. Likely causes:

- The Atlas cluster was deleted, paused, or renamed since the string was issued.
- A typo in the cluster/shard hostnames.

**Action needed:** get the current connection string from the MongoDB Atlas dashboard
(Database → Connect → Drivers) and update `MongoDB:ConnectionString` in `appsettings.json`.
All application code (models, services, controllers, seeding) is otherwise verified to
build and run correctly.
