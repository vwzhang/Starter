# Enhanced Aspire Starter

[![CI](https://github.com/vwzhang/Starter/actions/workflows/ci.yml/badge.svg)](https://github.com/vwzhang/Starter/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![Aspire](https://img.shields.io/badge/Aspire-13.4-5C2D91)
![Blazor](https://img.shields.io/badge/UI-Blazor%20%2B%20MudBlazor-594AE2)
![PostgreSQL](https://img.shields.io/badge/Data-PostgreSQL%2018-336791)
![License](https://img.shields.io/badge/license-MIT-green)

An opinionated .NET 10 Aspire starter for building internal tools, admin portals, and full-stack line-of-business apps without spending the first day wiring infrastructure.

![Starter Workspace](docs/assets/starter-workspace.png)

## Why This Starter

This repository is meant to be cloned or used as a GitHub template when you want a real app foundation instead of a blank demo. It includes the pieces most teams add immediately: identity, roles, admin pages, PostgreSQL, Redis, migrations, local email capture, typed DTOs, a Minimal API, a Blazor frontend, and an integration test that starts the distributed app.

## Included

| Area | Included |
| --- | --- |
| App orchestration | .NET Aspire AppHost with Redis, PostgreSQL 18, pgAdmin, smtp4dev, API, Web, and migration service |
| Frontend | Blazor Web App, Interactive Server render mode, MudBlazor shell, module navigation, dark mode |
| Backend | Minimal API with service discovery and OpenAPI in Development |
| Data | PostgreSQL shared by API and Web, EF Core migrations, development seed data |
| Identity | ASP.NET Core Identity, seeded admin/manager/user accounts, roles, permissions, feature flags |
| Account flows | Self registration, configurable email confirmation, forgot/reset password |
| Admin | Users, roles, permissions, features, and system configuration |
| Email | SMTP settings, protected SMTP secret storage, smtp4dev local inbox |
| Developer samples | Copyable CRUD vertical slice with shared DTOs and database-backed API endpoints |
| Quality | GitHub Actions CI and Aspire integration smoke test |

## Architecture

```mermaid
flowchart LR
    AppHost["Starter.AppHost"] --> Web["Starter.Web<br/>Blazor + Identity"]
    AppHost --> Api["Starter.ApiService<br/>Minimal API"]
    AppHost --> Migrations["Starter.MigrationService"]
    AppHost --> Redis["Redis cache"]
    AppHost --> Postgres["PostgreSQL 18"]
    AppHost --> PgAdmin["pgAdmin"]
    AppHost --> Smtp["smtp4dev"]

    Web --> Shared["Starter.Shared DTOs"]
    Api --> Shared
    Web --> Postgres
    Api --> Postgres
    Migrations --> Postgres
    Web --> Redis
    Web --> Smtp
```

## Quick Start

Prerequisites:

- .NET 10 SDK
- Docker Desktop or another Docker-compatible runtime
- Aspire CLI

Start the full local stack:

```powershell
aspire start --apphost Starter.AppHost/Starter.AppHost.csproj
```

Common local URLs:

| Service | URL |
| --- | --- |
| Web | `https://localhost:7131` |
| Starter workspace | `https://localhost:7131/` |
| Dev CRUD sample | `https://localhost:7131/dev/crud` |
| pgAdmin | `http://localhost:5050` |
| smtp4dev | `http://localhost:5080` |
| Aspire Dashboard | Shown by `aspire start` |

If a port changes, check the Aspire dashboard or run:

```powershell
aspire describe --apphost Starter.AppHost/Starter.AppHost.csproj
```

## Seeded Accounts

The migration service seeds local test users in Development:

| Email | Role | Password |
| --- | --- | --- |
| `admin@starter.local` | Administrator | `Happy1..` |
| `manager@starter.local` | Manager | `Happy1..` |
| `user@starter.local` | User | `Happy1..` |

Open `/admin/login` or use the app bar login button.

## Admin And Configuration

The Admin module includes:

- Dashboard
- Users
- Roles
- Permissions
- Features
- System settings

System settings are stored in the `Settings` table and seeded from `Starter.Web/Services/SystemConfigurationService.cs`.

Useful runtime settings include:

- Self registration
- Require email confirmation
- Display development confirmation/reset links
- Public base URL
- SMTP delivery, host, port, SSL, username, password/API key

Secret settings, such as SMTP password or API key, are protected with ASP.NET Core Data Protection before being stored.

## Local Email

The AppHost runs smtp4dev so account emails can be tested without an external SMTP server.

Development defaults:

- Email delivery enabled
- SMTP host/port seeded from the Aspire smtp4dev endpoint
- SMTP SSL disabled
- SMTP username/password blank
- From address `no-reply@starter.local`

Use `http://localhost:5080` to view captured messages. Forgot password and email confirmation flows are both wired to the configured account email sender.

## Dev CRUD Module

The Dev CRUD module is a copyable vertical slice:

- DTOs: `Starter.Shared/DevTodoDtos.cs`
- Entity: `Starter.Web/Data/DevTodoItem.cs`
- EF configuration: `Starter.Web/Data/ApplicationDbContext.cs`
- Migration: `Starter.Web/Data/Migrations/*_DevTodoCrud.cs`
- API endpoints: `Starter.ApiService/DevTodoEndpoints.cs`
- Web client: `Starter.Web/DevTodoApiClient.cs`
- Blazor page: `Starter.Web/Components/Pages/Dev/Crud.razor`
- Navigation: `Starter.Web/Components/Pages/Dev/DevNav.razor`

API endpoints:

```text
GET    /dev/todos
GET    /dev/todos/{id}
POST   /dev/todos
PUT    /dev/todos/{id}
DELETE /dev/todos/{id}
```

To create a real feature module, copy the slice and rename the DTOs, entity, API route group, typed client, page, nav entry, and migration.

## Project Layout

```text
Starter.AppHost            Aspire orchestration
Starter.ApiService         Minimal API backend
Starter.Web                Blazor Web App, Identity, admin, dev pages
Starter.Shared             DTOs shared by API and Web
Starter.MigrationService   EF migrations and seed data
Starter.ServiceDefaults    Aspire service defaults
Starter.Tests              Aspire integration tests
```

## Useful Commands

Build everything:

```powershell
dotnet build Starter.slnx
```

Run tests:

```powershell
dotnet test Starter.slnx
```

Start Aspire:

```powershell
aspire start --apphost Starter.AppHost/Starter.AppHost.csproj
```

Stop Aspire:

```powershell
aspire stop --apphost Starter.AppHost/Starter.AppHost.csproj
```

Rebuild one running resource:

```powershell
aspire resource webfrontend rebuild --apphost Starter.AppHost/Starter.AppHost.csproj --non-interactive
```

Add a migration:

```powershell
dotnet ef migrations add MigrationName --project Starter.Web/Starter.Web.csproj --startup-project Starter.Web/Starter.Web.csproj --context ApplicationDbContext --output-dir Data/Migrations
```

## Development Vs Production Defaults

Development is intentionally convenient:

- Self registration enabled
- Email delivery enabled through smtp4dev
- Password reset links can be displayed on screen
- Email confirmation links can be displayed on screen when confirmation is enabled
- Seeded users available with a simple shared password

Production defaults are more conservative:

- Self registration disabled
- Email delivery disabled until configured
- Development confirmation/reset links disabled

## Search Keywords

`dotnet aspire starter`, `aspire template`, `blazor admin starter`, `aspnet core identity`, `mudblazor dashboard`, `postgresql aspire`, `minimal api starter`, `smtp4dev`, `ef core migrations`, `redis output cache`.

## License

MIT
