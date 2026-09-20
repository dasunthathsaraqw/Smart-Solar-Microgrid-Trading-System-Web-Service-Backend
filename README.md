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

- `MongoDB:ConnectionString` / `MongoDB:DatabaseName` — currently set to a local MongoDB
  instance (`mongodb://localhost:27017/`) for development. Point this at your own local
  MongoDB (e.g. via MongoDB Compass) or a working Atlas connection string.
- `Jwt:Key` / `Jwt:Issuer` / `Jwt:Audience` / `Jwt:ExpiryMinutes` — JWT signing settings.
  `Jwt:Key` in `appsettings.json` is a placeholder — it is **not** a real secret and the
  app will refuse to sign tokens with it as-is.

### Setting your own JWT secret locally

The real signing key is kept out of source control. Create an `appsettings.Development.json`
(gitignored) in this folder with your own key:

```json
{
  "Jwt": {
    "Key": "<any random string, at least 32 characters>"
  }
}
```

This overrides the placeholder in `appsettings.json` when `ASPNETCORE_ENVIRONMENT=Development`
(the default for `dotnet run`). Do not commit this file or paste real keys into
`appsettings.json`.

## Seed data

On first startup, if the `Users` collection is empty, a default Backoffice account is created:

- Email: `admin@smartsolar.com`
- Password: `Admin@123`
