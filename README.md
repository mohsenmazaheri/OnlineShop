# OnlineShopping

A fast, secure, multilingual online shop for small businesses: **.NET 10 Web API + PostgreSQL** backend and a
**Next.js PWA** storefront, in one repository. Runs on Linux and Windows (Docker).

- Plan and decisions: [docs/ROADMAP.md](docs/ROADMAP.md) (Persian) · SEO/marketing design: [docs/SEO-MARKETING.md](docs/SEO-MARKETING.md)
- Deploying a shop: [deploy/README.md](deploy/README.md)
- Working on the code (also for AI assistants): [CLAUDE.md](CLAUDE.md)

```
src/       .NET: Domain, Application, Infrastructure, Infrastructure.Migrations.PostgreSql, Api
apps/web/  Next.js storefront (PWA)
tests/     .NET unit and integration tests
deploy/    Dockerfiles, docker-compose, nginx example
docs/      roadmap and design documents
```

## Development quick start

Requirements: .NET SDK 10, Node.js 26 LTS, PostgreSQL 18.

```bash
dotnet tool restore
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Database=onlineshop_dev;Username=...;Password=..." --project src/OnlineShopping.Api
dotnet run --project src/OnlineShopping.Api      # http://localhost:5087, migrates the dev database
cd apps/web && npm install && npm run dev        # http://localhost:3000
```

**One F5 for both:** in Visual Studio pick the **"API + web"** launch profile of `OnlineShopping.Api` (the first
profile). It starts the API, runs `npm run dev` in `apps/web` (unless the storefront is already running), opens the
browser on the storefront and stops Next.js when debugging stops. The same from a terminal:
`dotnet run --project src/OnlineShopping.Api --launch-profile "API + web"`. Run `npm install` in `apps/web` once first.
The `http` profile starts the API alone. If the connection string is missing, startup stops with the exact
user-secrets file it looked for.

**Test accounts (local only):** in Development with a local public URL, the API creates two demo accounts with
confirmed emails: `customer@shop.localhost` (customer) and `admin@shop.localhost` (Admin). The shared test password is
in `src/OnlineShopping.Api/appsettings.Development.json` (`DemoUsers`). They are never created in any other
environment or with a non-local public URL. Emails in Development are not sent: they are saved as `.eml` files in
`src/OnlineShopping.Api/App_Data/mail/` (SMS codes in `App_Data/sms/`).

Tests: `dotnet test` (integration tests need `IntegrationTests:AdminConnection`, see CLAUDE.md) and `npm test` in `apps/web`.
