# FMB Tiffin Tracker — Hussaini Alam

An ASP.NET Core 8 application with separate MVC and REST API hosts, CQRS scan processing (MediatR), and EF Core persistence. SQLite is the local-development default; SQL Server is selected when `ConnectionStrings:DefaultConnection` contains a SQL Server connection string.

## Features

- Household master: ITS ID, address/zone, contact, size, active/on-hold/discontinued status, RFID tag, and optional bulk distributor assignment.
- Distributor master with assigned-household counts and guarded deletion.
- Gate scan ingestion with reader timestamp, gate ID, source, same-day duplicate protection, and audit entries for successful, duplicate, held, and unknown-tag scans.
- Daily reconciliation with automatic status and audited manual collection (a reason is mandatory).
- Date-, zone-, status-, and entry-type-filtered daily/monthly trends, zone comparison, and distributor expected-versus-collected reports.
- Daily menu, expense, member query, feedback, system-user and audit-log registers with filtering, CSV exports, and audited record actions.
- Public daily-menu feedback form with WhatsApp sharing, member lookup by ITS ID, and protected PIN hashes for Development user accounts.
- Role-aware sign-in: Household accounts open a member portal with today's menu, collection status, feedback, upcoming menus, and recent collection history. Staff receive built-in or custom role-specific dashboards; page and action permissions are checked server-side.
- Built-in and configurable staff roles with server-enforced module permissions, dashboard widgets, and role-filtered navigation. Staff PINs and household passwords are stored as PBKDF2 hashes.
- Responsive light/dark MVC interface, gate simulator, structured logs, and correlation IDs returned in `X-Correlation-ID`.

## Run locally

```powershell
dotnet run --project src\FMB.WebMVC\FMB.WebMVC.csproj
dotnet run --project src\FMB.WebAPI\FMB.WebAPI.csproj
```

The MVC and API projects have launch profiles in `Properties\launchSettings.json`. The API listens at `http://localhost:49918` and Swagger is available at `/swagger`. In local development, both hosts use the same SQLite database at `src\FMB.WebMVC\fmb_local.db`. The API connection is configured relative to its content root so gate scans are visible to the MVC dashboard and operational screens. Override `ConnectionStrings__DefaultConnection` to use SQL Server or another deployment-specific database.

In Development, the MVC login supports staff ITS IDs `30455616` (Murtaza Sagarwala, Admin), `40000002` (Zoeb Merchant, Volunteer), `40000003` (Taher Lakdawala, Back Office), `40000004` (Hatim Contractor, Volunteer), and `40000005` (Juzer Vora, Back Office), all with password `1234`. Prototype household records can sign in with their Thali number or ITS ID and password `1234`; for example, ITS ID `30400000`. Demo accounts are disabled outside Development; configure account passwords for production use.

Development seeding additively imports 370 prototype households, five distributors, 45 days of collection history, today's and upcoming menus, and representative expenses, feedback, queries, and audit entries. The sample timeline rolls forward with the current date so the dashboard and date-sensitive operations remain usable. Existing records are not overwritten.

The authenticated management navigation is filtered by staff role. Admins can create and edit custom roles, customize the Volunteer and Back Office role permissions and dashboard widgets, and manage system users. The Admin role cannot be edited or deleted; other built-in roles cannot be deleted, and a custom role assigned to a user cannot be deleted. Role permissions are enforced server-side and selected dashboard widgets are shown to staff. Role pages include permission details and role-specific activity logs. User managers can assign only roles whose permissions do not exceed their own. Volunteers focus on gate and reconciliation work; Back Office users focus on expenses, feedback, and member queries. Admins can schedule the same menu across a date range; existing menu entries and finalized days are skipped and the bulk operation is audited. Reconciliation can close a published menu's distribution window, then finalize the day; finalization locks subsequent gate scans, manual collection corrections, and menu edits/deletions for that date. Feedback links are date-specific and publicly accessible; responses are associated with a registered household and the menu for that date. List pages support CSV download, with spreadsheet formula-leading text escaped in exports. Editing a system user leaves the existing PIN unchanged unless a replacement is entered.

## Gate API

`POST /api/gate/scan` accepts:

```json
{
  "TagID": "RFID-1001",
  "Timestamp": "2026-11-15T21:18:00+05:30",
  "GateId": "MAIN-GATE",
  "Source": "Auto"
}
```

The payload timestamp is retained as the collection event time. Unknown tags return `404`, households on hold return `409`, and scan validation errors return `400`; responses use Problem Details for API errors. `GET /api/gate/stats` returns daily collection totals and accepts an optional `date` query parameter. See [FMB.WebAPI.http](./src/FMB.WebAPI/FMB.WebAPI.http) for runnable examples.

## Database configuration

For SQL Server, set the `ConnectionStrings__DefaultConnection` environment variable to a SQL Server connection string. Do not commit credentials. New databases are initialized on startup; sample records are seeded only in Development. The initializer creates the operational feedback and system-user tables additively for existing local SQLite and SQL Server databases. Deployments that need controlled schema evolution should use reviewed EF Core migrations and a dedicated migration step rather than relying on startup initialization.

## Tests

```powershell
dotnet test FMB.TiffinTracker.sln
```

## Production deployment note

Production sign-in is intentionally not backed by the Development demo accounts. Configure an approved identity provider and authorization policies before deployment. Outside Development, the API also requires a `Gate__ApiKey` secret and validates it through the `X-Gate-Api-Key` header. Enforce HTTPS, set a production SQL Server connection string through a secret manager, review CORS policy for the gate network, and deploy schema changes through migrations. The demo UI, seed records, and startup database initialization are for local development and are not a substitute for deployment-specific identity, authorization, backup, and migration operations.
