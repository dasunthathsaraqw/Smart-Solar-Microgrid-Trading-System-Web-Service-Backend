# IIS deployment (Windows, .NET 10)

These steps deploy the API as an IIS site at the server root. If hosted as an application under another site, include its application path before `/api/...` in every URL. Use a private test database for sample data.

1. Install IIS with the ASP.NET Core Module via the **.NET 10 Hosting Bundle** on the Windows server. Install or provision MongoDB (local service or Atlas), allow the server to reach it, and run `iisreset` from an elevated terminal after installing the Hosting Bundle. Confirm `dotnet --list-runtimes` shows ASP.NET Core 10. [Microsoft IIS hosting guide](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0)
2. From this repository, publish: `dotnet publish -c Release -o C:\Deploy\SmartMicrogridApi`. Copy the entire published directory to the IIS server if publishing elsewhere. Keep the published `web.config` beside `SmartMicrogrid.API.dll`.
3. In IIS Manager create a dedicated application pool with **.NET CLR version: No Managed Code** and **Managed pipeline mode: Integrated**. Create a site (or application) whose physical path is the publish directory and assign that pool. Grant its identity read/execute access to the directory. Do not enable sample seeding in production.
4. Set production configuration for the IIS worker process. The recommended isolated-app-pool option is to edit the pool's Environment Variables in IIS; alternatively add these inside the existing published `web.config` `<aspNetCore ...>` element (do not add a second element):

   ```xml
   <environmentVariables>
     <environmentVariable name="Jwt__Key" value="YOUR_PRIVATE_RANDOM_KEY_AT_LEAST_32_CHARACTERS" />
     <environmentVariable name="MongoDB__ConnectionString" value="mongodb://localhost:27017/" />
     <environmentVariable name="Cors__AllowedOrigins__0" value="http://YOUR-WEB-APP-HOST" />
   </environmentVariables>
   ```

   Replace all example values. Use a MongoDB credentialed connection string or Atlas URI as appropriate. Keep `Jwt__Key` private, limit file/setting access to administrators and the app-pool identity, and never commit real values. The double underscore maps to nested .NET configuration keys. Add more browser origins as `Cors__AllowedOrigins__1`, etc.; include the exact scheme and port of the deployed web app. Android apps are not browsers and need no CORS entry. `Hosting__UseHttpsRedirection` defaults to `false`, which permits HTTP calls from LAN Android clients. Only set it to `true` when a trusted TLS endpoint is configured. HTTP transmits credentials and tokens in cleartext; restrict it to a controlled demo network. [Microsoft IIS environment-variable guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/web-config?view=aspnetcore-10.0)
5. Bind the IIS site to **All Unassigned** IP addresses on port **80**, or another selected port. Add a Windows Firewall inbound TCP rule for that port, for example in an elevated PowerShell session: `New-NetFirewallRule -DisplayName 'Smart Microgrid API HTTP' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 80`. Ensure the phone and server can communicate on the LAN. For public deployment, configure a trusted certificate and HTTPS instead.
6. Recycle the app pool. Verify `http://localhost/api/health` on the server, then `http://<LAN-IP>/api/health` from a physical phone browser. Both should return `{"status":"ok","database":"ok"}`. If using a non-default port, include `:<port>`. An Android emulator reaches the host PC at `http://10.0.2.2:<port>`; a physical phone uses the PC's LAN IP. If health returns 503, investigate MongoDB connectivity.

## Troubleshooting and logs

The API uses Console and Debug logging only; it does not require Windows Event Log write permission. For IIS startup diagnostics, temporarily set `stdoutLogEnabled="true"` and `stdoutLogFile=".\logs\stdout"` on the published `web.config` `<aspNetCore>` element. Give `IIS AppPool\<pool-name>` write permission to the logs directory. Reproduce the error, inspect the generated log, then disable stdout logging again and remove sensitive diagnostic logs according to your retention policy. [Microsoft ASP.NET Core Module logging reference](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/aspnet-core-module?view=aspnetcore-10.0)

- **500.19**: invalid/missing IIS configuration or ASP.NET Core Module; inspect the detailed IIS error and `web.config`.
- **500.30**: in-process app failed to start; check stdout logs, the .NET 10 runtime, and `Jwt__Key` (placeholder/short keys fail fast).
- **502.5**: out-of-process app failed to start or listen; inspect stdout logs and process/runtime configuration.
- **503 from `/api/health`**: process is up, but MongoDB ping failed; check the connection string, service and network rules.

These status meanings and stdout behavior follow the [ASP.NET Core Module documentation](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/aspnet-core-module?view=aspnetcore-10.0).
