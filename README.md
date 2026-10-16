# TruckMate - Weighbridge Management System

TruckMate is an ASP.NET Core MVC application for recording and managing vehicle weighing transactions. It supports first and second weighings, ticket generation, operator authentication, reports, audit logging, configurable system settings, and simulated scale integration for development.

## Features

- Operator, administrator, and super-administrator roles
- Cookie-based authentication and access control
- First and second vehicle weighings
- Pending, completed, and not-completed transaction states
- Automatic ticket numbering
- Ticket PDF generation with QuestPDF
- PDF and Excel report exports
- Material, supplier, transporter, driver, and subtype master data
- Audit logging for important actions
- Configurable pending expiry, manual-save/reprint reasons, branding, and output folders
- Scale, indicator, camera, and barrier-controller integration points
- Development scale simulation endpoints
- SQLite database for local development
- Automatic database creation and seed data on application startup

## Technology Stack

- .NET 10
- ASP.NET Core MVC
- Entity Framework Core 10
- SQLite by default
- SQL Server provider available
- Cookie authentication
- QuestPDF for ticket documents
- Bootstrap and jQuery assets under `wwwroot/lib`

## Requirements

- .NET SDK 10.0 or later
- Windows is recommended when connecting to serial weighbridge hardware
- A supported browser such as Microsoft Edge, Google Chrome, or Firefox

Check the installed SDK:

```powershell
dotnet --version
```

## Getting Started

**Visual Studio 2026 (18.0 or later):** open `TruckMate.sln`, pick a launch profile from the toolbar (`http` for the mock scale, `http (real scale)` for weights from the agent or simulator, `selftest` for the self-test) and press Ctrl+F5. Visual Studio 2022 cannot target .NET 10.

Command line:

1. Clone the repository:

   ```powershell
   git clone https://github.com/Mashi333/Weighbridge-Management-System.git
   cd Weighbridge-Management-System
   ```

2. Restore dependencies:

   ```powershell
   dotnet restore TruckMate.sln
   ```

3. Run the application:

   ```powershell
   dotnet run
   ```

4. Open the HTTPS or HTTP URL shown in the terminal. The development launch profile normally uses:

   ```text
   https://localhost:7xxx
   http://localhost:5xxx
   ```

   The exact ports are defined in `Properties/launchSettings.json`.

The application creates `truckmate.db` automatically on first startup and seeds development data, users, materials, master data, settings, and sample transactions.

## Development Login Accounts

The first startup creates these development accounts:

| Role | Username | Password |
| --- | --- | --- |
| Operator | `operator` | `Operator@123` |
| Operator | `operator2` | `Operator@123` |
| Admin | `admin` | `Admin@123` |
| Super Admin | `superadmin` | `Super@123` |

These credentials are stored in the source seeder for local development. Change or replace them before deploying to a shared or production environment.

## Configuration

The default configuration is in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Default": "Data Source=truckmate.db"
  }
}
```

The application also loads `appsettings.Development.json` when running in the Development environment.

ASP.NET Core environment variables can override configuration values. For example, PowerShell can override the database connection string without editing the JSON file:

```powershell
$env:ConnectionStrings__Default = "Data Source=truckmate-dev.db"
dotnet run
```

The application does not currently require a `.env` file. Use environment variables, deployment configuration, or a secret manager for production secrets and connection strings.

Additional operational settings, including scale ports, ticket output behavior, branding, and pending expiry, are stored in the `SystemSettings` table and can be managed through the application by an authorized administrator.

## Database and Generated Files

The default database is:

```text
truckmate.db
```

The database is created automatically through `EnsureCreatedAsync()` during startup. Local database files, build output, logs, and IDE-specific files are excluded by `.gitignore`.

For a clean local reset, stop the application and remove the database files, then run the application again:

```powershell
Remove-Item .\truckmate.db, .\truckmate.db-shm, .\truckmate.db-wal -ErrorAction SilentlyContinue
dotnet run
```

This deletes local data and recreates the seeded database. Do not use this procedure for a production database.

## Scale Integration

Weight comes from the weighbridge indicator through a small edge agent:

```text
Indicator --RS-232--> Edge agent (EdgeAgent/, Windows service) --HTTP POST /api/weight--> Server
Server: WeightCache -> AgentScaleService (IScaleService) -> operator pages
```

The server is the only source of truth for weight. Save actions read the weight on the server and never accept a weight from the browser. A weight is saved only when the indicator is online, the reading is stable, it is no older than `StaleAfterSeconds`, and it is at least `MinWeightKg`. Proceed is allowed only when the stable reading is within `ZeroToleranceKg` of zero.

### Mock or real scale

The `Scale` section of `appsettings.json` controls this:

| Setting | Meaning | Default |
| --- | --- | --- |
| `UseMock` | `true` uses `MockScaleService` and shows the operator's scale simulator panel. Only allowed in Development; the app refuses to start otherwise. | `false` (`true` in Development) |
| `ScaleId` | Weighbridge this server reads. Must match the agent's `ScaleId`. | `WB1` |
| `ZeroToleranceKg` | How far from 0 an empty scale may read. Set to one or two scale divisions. | `0` |
| `MinWeightKg` | Lowest weight that can be saved. | `100` |
| `MaxWeightKg` | Readings above this are rejected by the weight API. | `80000` |
| `StaleAfterSeconds` | Older readings count as "No signal". | `5` |
| `AgentApiKey` | Secret the agent sends in `X-Api-Key`. Empty means every reading is refused. Set it with the `Scale__AgentApiKey` environment variable in production. | empty (`dev-secret` in Development) |

To run the app against real weights during development:

```bash
Scale__UseMock=false dotnet run
```

### Indicator simulator

`Tools/indicator_sim.py` imitates a continuous-output indicator (Toledo `ST,GS,+001234.5kg` or plain `+  1234 kg` lines). Send it straight to the weight API:

```bash
python3 Tools/indicator_sim.py -o http --url http://localhost:5095/api/weight --api-key dev-secret --echo
```

Type `t 15000` (truck arrives), `l` (leaves), `p`/`r` (unplug/replug), `g` (garbage line), `q` (quit). Serial output (`-o serial --port ...`) needs `pip install pyserial`.

### Edge agent

`EdgeAgent/` is a .NET Worker Service. It reads the COM port, parses each line (`IndicatorParser.cs`), uses the indicator's `ST`/`US` flag for stability (or 5 identical readings when the line has no flag), and posts the latest reading about 4 times a second plus a 1-second heartbeat. Settings are in `EdgeAgent/appsettings.json` under `Agent`.

Run it locally against a virtual serial pair (macOS: `socat -d -d pty,raw,echo=0 pty,raw,echo=0`; Windows: com0com):

```bash
cd EdgeAgent
Agent__ComPort=/dev/ttys005 dotnet run
```

Publish and install on the weighbridge PC:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

```powershell
sc.exe create WeighAgent binPath= "C:\WeighAgent\WeighAgent.exe" start= auto
sc.exe failure WeighAgent reset= 86400 actions= restart/5000/restart/5000/restart/5000
sc.exe start WeighAgent
```

Set `ComPort`, `BaudRate`, `Parity`, `ServerUrl` and `ApiKey` in `C:\WeighAgent\appsettings.json` first. The parser and serial settings must be checked against a real sample from the site indicator before go-live.

## Useful Commands

Build:

```powershell
dotnet build TruckMate.sln
```

Run without rebuilding dependencies:

```powershell
dotnet run --no-restore
```

Run the application self-test:

```powershell
dotnet run -- --selftest
```

The `--selftest` argument is handled by the application and runs its built-in self-test flow.

## Project Structure

```text
TruckMate/
├── Controllers/       MVC controllers and HTTP endpoints
├── Data/              EF Core DbContext and database seeding
├── Domain/            Domain entities and enums
├── Models/            MVC view models
├── Services/          Business services and integrations
├── Views/             Razor views
├── wwwroot/           CSS, JavaScript, and static assets
├── Properties/        Launch profiles
├── EdgeAgent/         Windows service that reads the indicator and posts weights
├── Tools/             indicator_sim.py (indicator simulator for testing)
├── Docs/              Design documents
├── Program.cs         Application startup and dependency injection
├── TruckMate.csproj   Project and NuGet package configuration
├── appsettings.json   Base application configuration
└── appsettings.Development.json
```

## Production Considerations

Before production deployment:

1. Replace the seeded development accounts and enforce secure password management.
2. Store connection strings and secrets outside source control.
3. Use a production database strategy and backups instead of relying on local SQLite files.
4. Configure HTTPS certificates and secure cookie settings for the hosting environment.
5. Set `Scale:AgentApiKey` (environment variable), confirm `ZeroToleranceKg` and `MinWeightKg` for the site scale, install the edge agent, and configure camera and barrier integrations.
6. Set a production PDF output location with appropriate permissions.
7. Review authorization rules and audit-log retention.
8. Run the application behind a suitable ASP.NET Core host or reverse proxy.


 
