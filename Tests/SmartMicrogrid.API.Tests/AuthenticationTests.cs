/**
 * File: AuthenticationTests.cs
 * Purpose: Integration checks for role-compatible login and persisted current-user station context.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartMicrogrid.API.Models;
using SmartMicrogrid.API.Tests.Infrastructure;
using Xunit;

namespace SmartMicrogrid.API.Tests;

[Collection("Api")]
public sealed class AuthenticationTests
{
    private readonly ApiFactory _factory;
    private readonly TestHelpers _helpers;

    // Initializes authentication tests against the shared disposable API database.
    public AuthenticationTests(ApiFactory factory)
    {
        _factory = factory;
        _helpers = new TestHelpers(factory);
    }

    // Rule: login adds nullable station context without changing existing role-specific authentication data.
    [Fact]
    public async Task Login_AllRoles_ReturnsCompatibleIdentityAndOperatorStationContext()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        using var anonymous = _factory.CreateClient();
        var station = await _helpers.CreateStationAsync(admin);
        var assignedEmail = $"login-assigned-{Guid.NewGuid():N}@example.com";
        var unassignedEmail = $"login-unassigned-{Guid.NewGuid():N}@example.com";
        const string operatorPassword = "Operator@Test123";

        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/users", new
        {
            name = "Assigned Login Operator",
            email = assignedEmail,
            password = operatorPassword,
            role = "GridOperator",
            stationId = station.Id,
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/users", new
        {
            name = "Unassigned Login Operator",
            email = unassignedEmail,
            password = operatorPassword,
            role = "GridOperator",
        })).StatusCode);

        var assignedLoginResponse = await anonymous.PostAsJsonAsync(
            "/api/auth/login",
            new { email = assignedEmail, password = operatorPassword });
        var unassignedLoginResponse = await anonymous.PostAsJsonAsync(
            "/api/auth/login",
            new { email = unassignedEmail, password = operatorPassword });
        var backofficeLoginResponse = await anonymous.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "admin@smartsolar.com", password = "Admin@123" });

        var prosumer = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var prosumerClient = prosumer.Client;
        var prosumerLoginResponse = await anonymous.PostAsJsonAsync(
            "/api/auth/login",
            new { email = prosumer.Email, password = prosumer.Password });

        Assert.Equal(HttpStatusCode.OK, assignedLoginResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unassignedLoginResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, backofficeLoginResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, prosumerLoginResponse.StatusCode);

        var assignedLogin = await assignedLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        var unassignedLogin = await unassignedLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        var backofficeLogin = await backofficeLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        var prosumerLogin = await prosumerLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(station.Id, assignedLogin!.StationId);
        Assert.DoesNotContain(
            new JwtSecurityTokenHandler().ReadJwtToken(assignedLogin.Token).Claims,
            claim => string.Equals(claim.Type, "stationId", StringComparison.OrdinalIgnoreCase));
        Assert.Null(unassignedLogin!.StationId);
        Assert.Equal("Backoffice", backofficeLogin!.Role);
        Assert.Null(backofficeLogin.StationId);
        Assert.False(string.IsNullOrWhiteSpace(backofficeLogin.Token));
        Assert.Equal("System Admin", backofficeLogin.Name);
        Assert.Equal("admin@smartsolar.com", backofficeLogin.Email);
        Assert.True(backofficeLogin.ExpiresAt > DateTime.UtcNow);
        Assert.Equal("Prosumer", prosumerLogin!.Role);
        Assert.Null(prosumerLogin.StationId);
        Assert.Contains(
            new JwtSecurityTokenHandler().ReadJwtToken(prosumerLogin.Token).Claims,
            claim => claim.Type == "nic" && claim.Value == prosumer.Nic);
    }

    // Rule: /auth/me reloads assignment and role changes from MongoDB without requiring a replacement JWT.
    [Fact]
    public async Task Me_AssignmentAndRoleChanges_ReturnCurrentPersistedValuesWithSameToken()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var firstStation = await _helpers.CreateStationAsync(admin);
        var secondStation = await _helpers.CreateStationAsync(admin);
        var email = $"me-current-{Guid.NewGuid():N}@example.com";
        const string password = "Operator@Test123";
        var createResponse = await admin.PostAsJsonAsync("/api/users", new
        {
            name = "Current Assignment Operator",
            email,
            password,
            role = "GridOperator",
            stationId = firstStation.Id,
        });
        var created = await createResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);
        using var operatorClient = await _helpers.LoginAsync(email, password);
        var originalToken = operatorClient.DefaultRequestHeaders.Authorization!.Parameter!;

        var initial = await operatorClient.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(created.Id, initial.GetProperty("id").GetString());
        Assert.Equal(email, initial.GetProperty("email").GetString());
        Assert.Equal("GridOperator", initial.GetProperty("role").GetString());
        Assert.Equal(firstStation.Id, initial.GetProperty("stationId").GetString());

        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PutAsJsonAsync($"/api/users/{created.Id}", new { stationId = secondStation.Id })).StatusCode);
        var reassigned = await operatorClient.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(secondStation.Id, reassigned.GetProperty("stationId").GetString());
        Assert.Equal(originalToken, operatorClient.DefaultRequestHeaders.Authorization!.Parameter);

        Assert.Equal(
            HttpStatusCode.OK,
            (await admin.PutAsJsonAsync($"/api/users/{created.Id}", new { role = "Backoffice" })).StatusCode);
        var changedRole = await operatorClient.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("Backoffice", changedRole.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, changedRole.GetProperty("stationId").ValueKind);
    }

    // Rule: unassigned operators receive null current-user station context while anonymous callers remain unauthorized.
    [Fact]
    public async Task Me_UnassignedOperatorAndAnonymous_ReturnNullAndUnauthorized()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var operatorAccount = await _helpers.CreateGridOperatorAsync(admin);
        using var operatorClient = operatorAccount.Client;
        using var anonymous = _factory.CreateClient();

        var current = await operatorClient.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("GridOperator", current.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, current.GetProperty("stationId").ValueKind);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/auth/me")).StatusCode);
    }

    // Rule: wrong passwords and inactive accounts retain the existing Unauthorized and Forbidden login responses.
    [Fact]
    public async Task Login_WrongPasswordOrInactiveAccount_PreservesExistingFailures()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        using var anonymous = _factory.CreateClient();
        var email = $"inactive-login-{Guid.NewGuid():N}@example.com";
        const string password = "Operator@Test123";
        var createResponse = await admin.PostAsJsonAsync("/api/users", new
        {
            name = "Inactive Login Operator",
            email,
            password,
            role = "GridOperator",
        });
        var created = await createResponse.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(created);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong password" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/api/users/{created.Id}/deactivate", null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await anonymous.PostAsJsonAsync("/api/auth/login", new { email, password })).StatusCode);
    }
}
