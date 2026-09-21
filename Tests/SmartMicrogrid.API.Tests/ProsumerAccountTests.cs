/**
 * File: ProsumerAccountTests.cs
 * Purpose: Integration checks for registration, token-owned profile changes and deactivation workflow.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using MongoDB.Driver;
using SmartMicrogrid.API.Models;
using SmartMicrogrid.API.Tests.Infrastructure;
using Xunit;

namespace SmartMicrogrid.API.Tests;

[Collection("Api")]
public sealed class ProsumerAccountTests
{
    private readonly ApiFactory _factory;
    private readonly TestHelpers _helpers;

    // Initializes account tests against the shared disposable API database.
    public ProsumerAccountTests(ApiFactory factory)
    {
        _factory = factory;
        _helpers = new TestHelpers(factory);
    }

    // Rule: registration creates matching inactive records, bad credentials stay 401, and approval enables a NIC-bearing token.
    [Fact]
    public async Task Register_PendingThenApproved_EnforcesLoginAndNicClaim()
    {
        using var anonymous = _factory.CreateClient();
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var nic = $"2000{Random.Shared.Next(10000000, 99999999):D8}";
        var email = $"pending-{Guid.NewGuid():N}@example.com";
        const string password = "Prosumer@Test123";
        var registration = await anonymous.PostAsJsonAsync("/api/prosumers/register", new
        {
            nic, name = "Pending User", email, password, contactNumber = "0771234567",
            address = "Colombo", panelCapacityKw = 5.5,
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var profile = await _factory.Database.GetCollection<Prosumer>("Prosumers").Find(p => p.Nic == nic).FirstOrDefaultAsync();
        var user = await _factory.Database.GetCollection<User>("Users").Find(u => u.Nic == nic).FirstOrDefaultAsync();
        Assert.NotNull(profile);
        Assert.NotNull(user);
        Assert.False(profile.IsActive);
        Assert.False(user.IsActive);
        Assert.Equal(profile.PasswordHash, user.PasswordHash);

        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.PostAsJsonAsync("/api/auth/login", new { email, password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong password" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/api/prosumers/{nic}/reactivate", null)).StatusCode);
        var login = await anonymous.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(token).Claims, claim => claim.Type == "nic" && claim.Value == nic);
    }

    // Rule: /me reads and updates only the caller's profile, synchronizing editable identity fields.
    [Fact]
    public async Task Profile_TwoProsumers_UpdatesOnlySignedInOwner()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var first = await _helpers.RegisterAndApproveProsumerAsync(admin);
        var second = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var firstClient = first.Client;
        using var secondClient = second.Client;
        var before = await firstClient.GetFromJsonAsync<ProsumerResponse>("/api/prosumers/me");
        Assert.Equal(first.Nic, before!.Nic);
        var newEmail = $"updated-{Guid.NewGuid():N}@example.com";
        var update = await firstClient.PutAsJsonAsync("/api/prosumers/me", new
        {
            name = "Updated Owner", email = newEmail, contactNumber = "0719876543",
            address = "Kandy", panelCapacityKw = 12.5,
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await firstClient.GetFromJsonAsync<ProsumerResponse>("/api/prosumers/me");
        var unchanged = await secondClient.GetFromJsonAsync<ProsumerResponse>("/api/prosumers/me");
        Assert.Equal("Updated Owner", updated!.Name);
        Assert.Equal(newEmail, updated.Email);
        Assert.Equal("0719876543", updated.ContactNumber);
        Assert.Equal("Kandy", updated.Address);
        Assert.Equal(12.5, updated.PanelCapacityKw);
        Assert.Equal(second.Nic, unchanged!.Nic);
        Assert.Equal(second.Email, unchanged.Email);
        using var newLogin = await _helpers.LoginAsync(newEmail, first.Password);
        Assert.Equal(HttpStatusCode.OK, (await newLogin.GetAsync("/api/prosumers/me")).StatusCode);
    }

    // Rule: changing a password invalidates the old credential and accepts the new one.
    [Fact]
    public async Task ChangePassword_ValidCurrentPassword_RevokesOldCredential()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var account = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var client = account.Client;
        using var anonymous = _factory.CreateClient();
        var change = await client.PutAsJsonAsync("/api/prosumers/me/password", new
        {
            currentPassword = account.Password,
            newPassword = "NewProsumer@Test123",
        });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login", new { email = account.Email, password = account.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/auth/login", new { email = account.Email, password = "NewProsumer@Test123" })).StatusCode);
    }

    // Rule: open reservations block a deactivation request; Backoffice can honor it only after they close.
    [Fact]
    public async Task RequestDeactivation_OpenReservations_BlocksUntilCompletedThenDisablesLogin()
    {
        using var admin = await _helpers.LoginAsync("admin@smartsolar.com", "Admin@123");
        var account = await _helpers.RegisterAndApproveProsumerAsync(admin);
        using var client = account.Client;
        var station = await _helpers.CreateStationAsync(admin);
        var slot = await _helpers.CreateSlotAsync(admin, station.Id, 30);
        var booking = await _helpers.BookAsync(client, station.Id, slot.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/api/prosumers/me/request-deactivation", null)).StatusCode);
        await _helpers.ApproveAsync(admin, booking.Reservation.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsync("/api/prosumers/me/request-deactivation", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/reservations/{booking.Reservation.Id}/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync("/api/prosumers/me/request-deactivation", null)).StatusCode);
        var pending = await admin.GetFromJsonAsync<List<ProsumerResponse>>("/api/prosumers/pending-deactivations");
        Assert.Contains(pending!, prosumer => prosumer.Nic == account.Nic && prosumer.DeactivationRequested);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/api/prosumers/{account.Nic}/deactivate", null)).StatusCode);
        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.PostAsJsonAsync("/api/auth/login", new { email = account.Email, password = account.Password })).StatusCode);
    }
}
