# CLAUDE.md

## Project overview

**OnlineShop**: a single-store online shop for small businesses. Cross-platform (Linux/Windows), mobile-first
PWA, fast and secure. Monorepo: **ASP.NET Core 10 Web API** on **PostgreSQL 18** (Clean Architecture: Domain /
Application / Infrastructure / Api) + **Next.js 16** storefront in `apps/web`. One Docker stack per shop; the same
images serve every shop.

**Plan and all recorded decisions: [docs/ROADMAP.md](docs/ROADMAP.md)** (Persian). Read it before starting, work on
one step at a time, mark the status there when a step is done. Done: phases 0 and 2–7 (7: cart, checkout, order lifecycle, invoices/returns, Web Push); phase 1 has a
"remaining" list. SEO/marketing design: [docs/SEO-MARKETING.md](docs/SEO-MARKETING.md) (acceptance checklist at its end).

Key decisions (details in the roadmap):
- EF Core code-first, **one migrations project per provider** (PostgreSQL now, `snake_case`; SQL Server/MySQL later).
  Provider-specific SQL stays in that provider's migrations/config.
- Roles: one-person shop (`Admin` = everything) or staff roles `Manager`, `Warehouse`, `Accountant`. No multi-vendor.
- Single-tenant (no `tenant_id`). B2C physical goods only (no POS, digital, B2B).
- Currencies/gateways per deployment (Europe EUR/USD; Armenia AMD/EUR/USD; Iran IRR + ZarinPal/Sadad/Behpardakht;
  Stripe/PayPal elsewhere; no Paddle). Prices in a base currency, converted with admin rates + per-currency rounding;
  an order stores its currency and rate. Money always carries a currency.
- **No runtime dependency on external services**: self-host fonts, icons, scripts, images (no CDN, no Google Fonts,
  no `next/font/google`). Exceptions: opt-in marketing tags after cookie consent, and the background exchange-rate
  update (`ExchangeRates:Provider`, never during rendering). Iran: hosted in Iran, SMS OTP + email, no external logins.
- Search behind `IProductSearch`, chosen by `Search:Provider` (Postgres default; OpenSearch/Elasticsearch optional).
  PostgreSQL is the source of truth; displayed price/stock always come from it.
- SMS optional (works with SMS off). Tax: one rate per shop, PDF invoices, no e-invoicing in v1.
- Branding and layout templates are admin-editable without rebuild. Languages fa, en, hy, de, ar (RTL fa/ar), adding
  one must be cheap; content is translated manually only, untranslated fields fall back to the default language.
  Light + dark theme.

## Non-negotiable rules

1. **Every new class gets a unit test** (.NET `tests/` xUnit; web Vitest `*.test.ts(x)` next to the file). Never
   finish with failing or missing tests.
2. **SOLID / Clean Code**: thin endpoints, logic in Application services behind interfaces, presentation-only
   components, constructor injection, `TimeProvider` (never `DateTime.UtcNow`). Api → Infrastructure → Application → Domain.
3. **IDOR**: ids from URL/query/body are checked against the current user in the query itself
   (`x.Id == id && x.CustomerId == userId`) → 404 for others; staff may bypass. User id only from claims.
4. **No secrets in Git**: user-secrets, git-ignored `deploy/.env`/`.env*`, env vars in production; keys in Options sections.
5. **Prices and totals are computed on the server.** Payment success only via server-side verify/webhook with signatures.
6. **Copyright**: Digikala-*inspired* layout only; never their logo, name, colours, icons, images, texts or CSS.
7. Identity generics always use **`AppUser`**, never `IdentityUser`.
8. **Every public page is SEO-complete**: server-rendered (works without JS), title, description, canonical,
   hreflang, `lang`/`dir`, JSON-LD matching the visible price/stock, no layout shift. Filter/sort URLs canonicalize to
   the plain page; never change a public URL without a 301; non-production is always `noindex`.
9. No vulnerable packages: `dotnet list package --vulnerable --include-transitive`, `npm audit --omit=dev`; pin a
   patched version when a transitive one is vulnerable.
10. **Nothing shop-specific at build time**: store settings come from the API per request (`loadStore()`) or runtime
    env vars; never in `next.config.ts` or at module scope.

## Layout and commands

```
src/OnlineShop.{Domain,Application,Infrastructure,Api}, src/OnlineShop.Infrastructure.Migrations.PostgreSql
tests/OnlineShop.UnitTests (xUnit, SQLite in-memory, TestHost) and .IntegrationTests (real PostgreSQL, db per class)
apps/web (Next.js: src/app/[locale], src/proxy.ts, src/lib, openapi/OnlineShop.Api.json → src/lib/api/schema.d.ts)
deploy/ (Dockerfiles, docker-compose.yml, .env.example, nginx.conf)   docs/ (ROADMAP.md, SEO-MARKETING.md, images/ = README screenshots)
Directory.Build.props (net10.0, warnings = errors), Directory.Packages.props (central versions), dotnet-tools.json
```

```bash
dotnet tool restore && dotnet build OnlineShop.sln     # build also rewrites apps/web/openapi/*.json
dotnet test OnlineShop.sln [--filter "FullyQualifiedName~X"]
dotnet run --project src/OnlineShop.Api [--launch-profile "API + web"]   # :5087, migrates the dev DB
dotnet tool run dotnet-ef migrations add <Name> --project src/OnlineShop.Infrastructure.Migrations.PostgreSql --startup-project src/OnlineShop.Api
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/OnlineShop.Infrastructure.Migrations.PostgreSql --startup-project src/OnlineShop.Api
# apps/web (Node at C:\Program Files\nodejs; add to PATH in Git Bash)
npm run dev | test | lint | typecheck | build | gen:api
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build
```

After a model change: migration + `has-pending-model-changes` reports none. After an API contract change: build,
`npm run gen:api`, commit `openapi/*.json` and `schema.d.ts` (CI checks both).

## Local environment (this machine)

- PostgreSQL 18 (`postgresql-x64-18`, :5432, `C:\Program Files\PostgreSQL\18\bin\psql.exe`). `onlineshop_dev` owned by
  the least-privilege login `onlineshop` (never `postgres`); `onlineshop_test` (CREATEDB) for `onlineshop_it_*` databases.
- Secrets (never print, never in tracked files): API user-secrets `ConnectionStrings:Default`,
  `Postgres:SuperuserPassword`, optional `Database:AdminEmail/AdminPassword`; integration tests
  `IntegrationTests:AdminConnection` (CI: `INTEGRATIONTESTS__ADMINCONNECTION`).
- The Claude app is MSIX: its `%APPDATA%` writes are virtualized, so `dotnet user-secrets set` from Claude never reaches
  Visual Studio. The user sets secrets in their own terminal.
- `DemoUserSeeder` (`customer@`/`admin@shop.localhost`, password in `appsettings.Development.json`) runs only in
  Development **and** with a local `Store:PublicUrl`; keep both guards. `DemoCatalogSeeder` (~22 products, all five
  languages, SVGs in `apps/web/public/demo/`, EUR prices converted) only when `DemoCatalog:Enabled` (dev, or
  `DEMO_CATALOG=true` demo deployments; never a real shop); runs once (`DEMO-*` SKUs); edit `DemoCatalogData`.
- No Docker locally (CI builds images). .NET SDK 11.0.100 builds the net10.0 projects; builds fail during VS installs.
- WinNAT may reserve 2984–3083 so port 3000 fails (`listen EACCES`): check `netsh interface ipv4 show
  excludedportrange protocol=tcp`; meanwhile use another port with `Store__PublicUrl` set to it.
- Browser pane: no service workers (test PWA in a real browser); while hidden, `<Suspense>` content stays unrevealed
  (use `curl`). `.claude/launch.json`: `api` (5087), `web` (3000).

## Backend conventions

- `UseSnakeCaseNamingConvention()`; Identity tables `users`, `roles`, … Raw SQL fragments are snake_case and valid in
  PostgreSQL **and** SQLite (`is_deleted = false`).
- `BaseEntity`: Guid v7 `Id`, `CreatedUtc`, `ModifiedUtc?`, `IsDeleted` (`AuditableEntityInterceptor`). `Remove()` =
  soft delete + global filter (`IgnoreQueryFilters()`); unique indexes `.HasFilter(NotDeletedFilter)`. Ids exist before
  saving, so **add new children to their DbSet explicitly** or EF treats them as existing rows (FK error).
- All `DateTime`s UTC (`timestamptz`); `decimal`s `numeric(18,2)` (costs `numeric(18,4)`, rates `numeric(28,12)`).
- Use cases depend on `IApplicationDbContext`; reads use `AsNoTracking()` + projection to records. Npgsql cannot
  project `ValueTuple`s (use records). SQLite has no APPLY: load nested per-row-translated collections with separate
  flat queries (`ProductQueries.GetAsync`) or the result-selector `SelectMany`. Raw reads use ADO.NET, not `SqlQuery<T>`.
- Errors: `Result<T>` + `AppError` (`ErrorKind`, stable `code` like `category.not_found`) → `ToHttpResult()` → RFC 9457
  with `code`. Every code needs a translation in every dictionary (a test scans Application). Groups: `account.*` →
  `account.errors`; `staff.*` → `admin.errors`; `catalog.*`, `import.*`, `seo.*`, `inventory.*` → `admin.catalogErrors`,
  `admin.importErrors`, `admin.seoErrors`, `admin.inventoryErrors`. Non-error strings must not look like codes.
- Endpoints: minimal APIs under `/api/v1` with `WithName` (operationId) + summary; admin under `/api/v1/admin/...`
  with a permission. `JsonStringEnumConverter` and `JsonNumberHandling.Strict` (keeps OpenAPI types clean).
- Options: bind + `ValidateDataAnnotations` + `ValidateOnStart`, read through `IOptions` at runtime (never
  `configuration.Get<T>()` while registering). Operator-overridable lists are comma-separated strings (lists merge by index).
- Logging with `[LoggerMessage]` (CA1848). Code style: file-scoped namespaces, primary constructors, `sealed`,
  `required`/`= default!`.
- Never "fix" the old names: Data Protection app name `OnlineShop` (would invalidate cookies), `onlineshop_*`
  databases, `onlineshop` login, `os_session`, `onlineshop-integration-tests` secrets id, image and npm names.
- Time-dependent tests replace `TimeProvider` with `FakeTimeProvider`, never sleep.

### Domain notes

- **Translations**: default language in the item's own columns, others in owned `*_translations` (keyed owner +
  locale); queries fall back to the own column; endpoints take `?locale=` → `StoreOptions.ContentLocale()`. Admin
  texts travel as `LocalizedText` per language (`LocalizedTexts`; empty name = not translated).
- **Specs** ("Attribute" is a reserved suffix): `ProductSpec` (kind Text/Number/Boolean/Option, flags) → `SpecOption`
  → `ProductSpecValue` (one typed column per kind). Text values are language-neutral; translatable words are options.
  Templates: owned `TemplateSpec` on `Category`/`SubCategory`. `SpecAdminService`: kind fixed and no delete while used.
- **Products**: stable `PublicNumber` (max incl. deleted + 1, never reused), slug per language (`Slugs.From`).
  `ProductVariant` (≤3 option specs, ≤100 variants, SKUs unique across products and variants). `Gtin`
  (`ProductCodes.NormalizeGtin`), `Mpn`, `WeightGrams`, per-language `MetaTitle/MetaDescription/Tags`. `Brand` (Restrict).
- **Currency**: `Store:Currency` (ISO 4217, default IRR) is the base; display currencies = rate per base unit,
  rounding step, decimals, symbol (IRT = toman). `ExchangeRateSyncWorker` + `IExchangeRateSource`.
- **Media**: only through `MediaService` — images by magic bytes, re-encoded to WebP 1200/600/300 (store the 1200 URL;
  category 600, logo 300; `MediaUrls.For` / web `imageVariant()`); videos as `{key}/video.mp4` checked by `Mp4Inspector`
  (Range served). Keys are never overwritten (immutable caching). Uploads: multipart `file`, `.DisableAntiforgery()`.
  Videos > 10 MB need `proxyClientMaxBodySize`, nginx `client_max_body_size` and `RequestSizeLimitAttribute`.
- **Import/export** (`Catalog/Transfer`, CSV + XLSX by MiniExcel): export with flat projections (owned translations
  explode an Include chain); import reuses the admin services in one transaction, a savepoint per row,
  `ChangeTracker.Clear()` after a failed row, rollback on any problem or `dryRun`; only present columns change.
- **Inventory**: **never write `Product.Stock`, `ProductVariant.Stock`, `Reserved` or `StockLevel` directly.**
  `StockLedger` is the only stock writer (immutable `StockMovement` with `UnitCost`, atomic `ExecuteUpdate` that keeps
  results ≥ 0, then syncs tracked copies); `SetTotalAsync` for "stock is N"; several postings inside
  `db.InTransactionAsync`. One active default `Warehouse`. References `auto:*`, `ST-{n}` (stocktake), `PR-{n}` (purchase).
  Acting user from `ICurrentUser`. Purchases move the weighted `AverageCost` (`stock_items`); low stock = total ≤
  `MinimumStock`. Stocktake differences = counted − stock at `CountedUtc`.
- **Reservations** (`StockReservationService`): available = Stock − Reserved (storefront `InStock`). Reserve all or
  nothing ("reserved + n where stock − reserved ≥ n"); commit/release/expiry first claim the row with a conditional
  UPDATE; commit sells via `StockLedger.TakeAsync`. Checkout reserves with reference `order:{number}`.
- Orders: `CustomerId` (null for guests) FKs to `users` without navigations (orders Restrict, carts cascade); `Number`
  unique from 1001; everything is a snapshot (items with SKU/variant label/price, method name, tax rate, contact and
  address: personal data, blanked on erasure); `OrderStatus` stored as text with a generated check constraint.
- **Checkout** (`Sales/CheckoutService`, `CheckoutEndpoints`, codes `checkout.*` → web `checkout.errors`, merged with
  `account.errors` for address codes): `GET /checkout`, `POST /checkout/quote` (refused discount reported, not failed),
  `POST /checkout/orders` (rate limit `checkout`; one transaction: number, `StockReservationService.ReserveAsync`,
  `DiscountCodeService.ClaimAsync`, order; refuses a total other than `ExpectedTotal`). Arithmetic only in
  `CheckoutPricing` (discount, shipping per started kg/free threshold, tax inside or added, rounded to the base
  currency's decimals). Admin: `ShippingMethodService` + `TaxSettingsService` (`/admin/shipping`, settings.manage,
  `shipping.*` → `admin.shippingErrors`), `DiscountCodeService` (`/admin/discounts`, discounts.manage, `discount.*` →
  `admin.discountErrors`). Web: `CheckoutForm` (amounts in the base currency, `baseCurrency()`), `lib/checkout/*`.
- **Order lifecycle** (`Sales/OrderLifecycleService`, `OrderQueries`, `OrderEndpoints`, codes `order.*` → web
  `orders.errors`): allowed changes only via `OrderTransitions` (Pending → Paid → Shipped → Completed; pickup Paid →
  Completed; Canceled only before payment); each claims the order with a conditional UPDATE, writes immutable
  `OrderEvent`s (internal = staff only; a cancel reason is public, other notes internal) and tells the customer via
  `OrderNotifier` (email in the order's locale; SMS for placed/shipped, Iranian providers only with configured
  templates). Paid commits the `order:{number}` reservation (re-reserves when it ran out). Reservation calls clear the
  change tracker: add events after them. `SalesMaintenanceWorker`: expired unpaid orders → Canceled every minute,
  abandoned guest carts hourly. Guest links signed by `IOrderLinks` (Data Protection, nothing stored):
  `/{locale}/order/{n}?key=` (noindex); customers `/account/orders`; staff `/admin/orders` (orders.view / orders.manage).
- **Invoices, returns, refunds**: Paid issues an `Invoice` (gapless `Number`, seller details copied from
  `SalesSettings`) in the same transaction; the invoice is a printable page (browser "Save as PDF", decided: no PDF
  library; `InvoiceDocument`, site chrome `print:hidden`). Paid orders can be canceled before shipping (`RestockAsync`:
  ledger `Return` movements). `ReturnService`: customer requests within `SalesSettings.ReturnDays` of `CompletedUtc`
  (quantities ≤ ordered − non-rejected returns), staff approve/reject/receive (receive restocks), `RefundAsync` records
  refunds (paid orders only, total ≤ order total). Addresses are joined with `joinParts` (never `formatList`: it adds "and").
- **Web Push** (optional, off without `WebPush:PublicKey/PrivateKey`; keys from `dotnet run --project src/OnlineShop.Api
  -- generate-vapid-keys`): `PushSubscriptionService` (`push_subscriptions`, personal data, ≤20 per customer, one per
  endpoint, moves to the signed-in account), `IWebPushSender` = `WebPushSender` (RFC 8291 `aes128gcm` + RFC 8292 VAPID
  in plain .NET crypto, `WebPushCrypto`/`Vapid`; 404/410 → subscription deleted). `OrderNotifier` pushes the email's
  subject/body to signed-in customers. Codes `account.push_*`. Web: `PushNotifications` on `/account`,
  `lib/push/client.ts`, `push`/`notificationclick` in `public/sw.js` (same-origin URLs only).
- **Cart** (`Sales/CartService`, `CartEndpoints`, codes `cart.*` → web `cart.errors`): server-side for guests too
  (`CartOwner`: user id, else the HttpOnly `os_cart` token, stored only as SHA-256 `GuestTokenHash`); the first cart
  request after sign-in merges it (`MergeAsync`) and drops the cookie. Lines keyed by `ItemId` (variant id, else product
  id). Carts hold quantities only: price and available (Stock − Reserved) are read live, changed lines get a
  `CartLineProblem` and leave the subtotal. Every answer sets readable `os_cart_n` (the badge, `lib/cart/count.ts`; call
  `notifyCartChanged()` after a change). Web: `AddToCartButton` (in `VariantPicker` for variant products),
  `CartContents` (converted unit price × quantity), `getCart` (`lib/cart/server.ts`, forwards both cookies).
- **Search** (`Application/Search`): `search_documents` per product and language (normalized by `SearchText.Normalize`),
  rebuilt from `search_outbox`, which `SearchOutboxInterceptor` fills on catalog SaveChanges (never enqueue by hand;
  `ExecuteUpdate` changes are not seen). `SearchIndexWorker` processes it; tests call `IndexSearchAsync()`.
  `PostgresProductSearch` (generated weighted tsvector + GIN, trigram on title, raw SQL in the migration) and
  `SimpleProductSearch` (other databases, unit tests). `ProductTextSearch`: code lookup → `ExactNumber`, synonyms,
  `KeyboardLayouts` (on the raw text), "did you mean"; `SearchTermLog`; `SearchSuggestionService`.
- **External engine** (`Search:Provider` OpenSearch/Elasticsearch, `Search:Engine`): one REST client
  (`HttpSearchEngine`, bodies in `SearchEngineRequests`), alias `{prefix}-{locale}` → index `{alias}-{17-digit stamp}`.
  `IProductSearch` is then `EngineProductSearch` (engine while `SearchEngineHealth.CanServe`, else the
  `IDatabaseProductSearch`). `SearchIndexService` queues rebuilt products in `search_engine_outbox` (Guid.Empty = full
  rebuild into new indexes + alias switch); `SearchEngineSyncService` sends them; the worker sets ready/behind/down.
  Engine integration tests run only with `IntegrationTests:OpenSearchUrl`/`ElasticsearchUrl` (CI service containers).
- **Browse** (`IProductBrowse`, `GET /catalog/browse`): scope + filters + disjunctive facet counts in memory over small
  rows; `BrowseQueryParser` keys `c s b q brand group min max stock sale o.{spec} n.{spec}=min-max y.{spec} sort page`.
- **Compare** (`Catalog/Compare`): `ProductComparisonService` (≤4 numbers, `IsComparable` specs by group, variant
  options as texts, `CompareValue.None` for missing, `Best` from `ProductSpec.Better` Higher/Lower on numbers) and
  `CompareListService` (`compare_items`, personal data). `GET /api/v1/account/compare` answers guests 200
  `signedIn:false` (the browser asks; no 401 noise). Web: `lib/compare/{list,store,format}.ts` (guest list in
  localStorage `os_compare`, merged into the account after sign-in), `CompareButton` (link without JS), `CompareBar`
  (layout), page `/{locale}/compare?ids=&diff=1` (noindex, uncached prices).
- **Reviews** (`Catalog/Reviews/ReviewService`, `reviews.moderate`): one per customer and product (unique filtered
  index; rewriting → Pending), confirmed email/phone only, no links (`HasLink`), ≤5 an hour + `reviews` rate-limit
  policy; only Approved shows and counts. **Never write `Product.RatingAverage/RatingCount` directly**:
  `ReviewService.UpdateRatingAsync(db, productId)` after every change (also on account erasure). Codes `review.*` →
  web `reviews.errors`. Web: `ProductReviews` (SSR, `?reviews=N`), `ReviewComposer` (asks the API only when opened),
  JSON-LD `aggregateRating` + `review` from the visible ones, `/admin/reviews`; cache tag `reviews` (refreshable).
- **Wishlist** (`Customers/WishlistService`, `wishlist_items`, personal data): `WishlistButton` on the product page
  (`GET /account/wishlist/ids` answers guests 200 `signedIn:false`), page `/{locale}/account/wishlist`.
- **Sitemaps**: `/sitemap.xml` is an index route (`app/sitemap.xml/route.ts`) of `/sitemaps/sitemap/{id}.xml`
  (`app/sitemaps/sitemap.ts`, `generateSitemaps`; `/sitemaps/` is unlocalized in `routing.ts`), data from
  `SitemapService` (translated languages only, product images/videos). Add a new kind of public page there.
- **Landing pages** (`LandingPage`, `LandingPageService`, content.manage): `/{locale}/l/{slug}` lists a fixed browse
  query (`LandingQuery.Normalize` turns a pasted listing address into it; prices in base currency) and is indexed.
- **Special offers**: `Product.SaleEndsUtc` (only with `CompareAtPrice`); `SaleExpiryService`/`SaleExpiryWorker`
  restore the old price after the end. Web fetchers holding offer times cache with `cacheForOffers`/`saleAwareLife`
  so no page shows an ended offer; `Countdown` renders only a label on the server (the clock is browser time).
- **Blog & content pages** (`Domain/Content/ContentPage`, `ContentService`, content.manage, codes `content.*` →
  `admin.contentErrors`): posts at `/{locale}/blog/{slug}`, pages at `/{locale}/{slug}` (`app/[locale]/[page]`); a
  new top-level route must be added to `ContentService.ReservedSlugs`. Bodies use the safe marks of
  `lib/content/markdown.tsx` (never HTML). Cache tag `content` (refreshable); footer pages via `getFooterPages`.
- Redirects (`RedirectService`): decoded paths, no trailing slash, never `/`, `/api`, `/_next`, one hop, no loops.

## Accounts and authorization

- Accounts by **email or phone** (each unique; filtered unique indexes); `UserName` is random. Phones E.164 via
  `IPhoneNumberNormalizer` (`Store:PhoneRegion`). Phone sign-in = sign-up (verified, password-less); password sign-up
  is email only. Only salted code hashes are stored.
- Session cookie `os_session` (HttpOnly, Lax, Secure unless `Auth:SecureCookies=false`) from the API through the web
  proxy. The API answers 401/403, never redirects. CSRF: `OriginCheckMiddleware` (Origin must equal `Store:PublicUrl`).
  `PublicOriginMiddleware` makes the API see the public scheme/host (integration tests use an `http://` public URL).
- Endpoints require **permissions** (`Permissions.Policy(...)`), mapped from roles in `RolePermissions` (Admin = all).
  User id only via `User.GetUserId()`.
- Validation: FluentValidation + `.Validate<T>()` → 400 `errors: { field: [codes] }`. No account enumeration
  (same answer for wrong identifier/password; forgot-password always 202). `auth` rate-limit policy; 429 via
  `ErrorKind.TooManyRequests`.
- Email `IEmailSender` (`Smtp`, or `Pickup` .eml in dev); links always `Store:PublicUrl/{locale}/…`. SMS `ISmsSender`
  (Kavenegar, SmsIr, Twilio, Pickup, None default), gated by `ISmsCapability.IsEnabled`; keep `HttpClient` logging at
  Warning (Kavenegar's key is in the URL).
- External logins: auto-link only when the provider verified the email (only Google's `email_verified`); the last
  sign-in method cannot be unlinked; providers without credentials are never offered. Callback
  `/api/v1/account/external/{provider}/oauth-callback`.
- 2FA (TOTP + 10 recovery codes + remember-device): every sign-in path honours it (`SignInWithSecondStepAsync`); 403
  `account.two_factor_required`, then `/2fa/verify` or `/recovery`. `Security:RequireStaffTwoFactor` requires `amr=mfa`.
  QR is generated in the browser. `SignInClaims.PreserveOnRefresh` keeps `auth_time`/`amr`/provider across the
  security-stamp refresh; use `SignInClaims.AuthTime(user)` for "signed in recently".
- Account deletion **erases**: personal rows hard-deleted (`ExecuteDeleteAsync`), the `users` row stays without data
  (`ErasedUtc`). **Any new table with personal data must be cleared in `PersonalDataService.DeleteAsync` and exported
  in `ExportAsync`.**
- Staff (`StaffService`): invitations need a matching **verified** email/phone; never act on yourself; always one
  active Admin; audit every change; suspension renews the stamp and locks out.
- Redirects back into the storefront: `ReturnPaths.Sanitize` (API) / `safeReturnPath()` (web).

## Frontend conventions (Next.js 16 — read node_modules/next/dist/docs before using unfamiliar APIs)

- Cache Components + Partial Prefetching. Fetchers: `"use cache"` + `cacheTag` + `cacheLife`; invalidate with
  `revalidateTag(tag, "max")` via `POST /api/revalidate`; admin saves call `refreshStorefront(locale, tags)`.
- Store/runtime data only inside `<Suspense>` after `await connection()` (`loadStore()`), so the build needs no API.
  Dynamic route params (`[number]`) are read inside Suspense too. A page without per-request content renders
  `<Suspense><RequestTimeMarker /></Suspense>` (else "uncached data in generateMetadata()").
- `src/proxy.ts` (was middleware): API forwarding, redirects table, locale redirects, product slug 301s, security
  headers, noindex, sign-in redirects for `/account/*` and `/admin`. Static CSP (`'unsafe-inline'` scripts, rest `'self'`).
- Server-only modules import `"server-only"`; server components call the API with `apiGet`; browser code only posts
  JSON to `/api/v1/*` (`postJson`) or fetches public endpoints such as suggestions.
- UI strings in `src/lib/i18n/dictionaries/<locale>.json`, same keys as `fa.json` (tested).
- Design system: CSS tokens in `globals.css` (light, `[data-theme=dark]`, no-JS fallback); token classes
  (`bg-surface`, `text-muted`, `bg-accent`, `bg-inverse`, `tint-0…7`) and `src/components/ui/` (`Button`, `Card`,
  `Badge`, `Skeleton`, `Icon` = our own inline SVGs). No raw palette colours, icon fonts or CDNs.
- Admin: sections listed once in `src/lib/admin/sections.ts` (texts in every dictionary); pages under
  `app/[locale]/admin/` in `AdminShell`, calling `requirePermission()`/`requireStaff()` inside Suspense.
- Product URLs `/{locale}/p/{number}/{slug}`; canonical + hreflang only for translated languages (`productAlternates`),
  JSON-LD from the same response as the visible price. Listings `/c/{cat}[/{sub}]`, `/b/{brand}`, `/search`
  (`lib/catalog/browse.ts`, `listing-page.ts`): facet links `rel=nofollow`, ranges as GET forms, filtered/sorted →
  `noindex,follow`, canonical = plain list (page ≥ 2 keeps `?page`). Streamed pages cannot change HTTP status.
- Money: `formatMoney`; display currency via `loadDisplayCurrency(locale)` (cookie `os_currency`) and
  `convertAmount`/`displayMoney` — never store or send converted prices (convert typed prices back to base).
- Theme: `<head>` script sets `data-theme` before paint; never read the theme on the server. Fonts via
  `next/font/local` from npm packages (Vazirmatn first, Inter, Noto Sans Armenian). Formatting with `Intl`
  (`formatDate`, `formatList`; Persian calendar/digits for fa).
- Home page follows the design canvas "طراحی صفحه اول فروشگاه"; rails show API products only (never hard-coded).
- Tests: Vitest (`environment: node`; `// @vitest-environment jsdom` for components) + Testing Library.

## Commits

Messages start with a Solar Hijri date, e.g. `1405-08-17 Fix soft delete and audit fields`.

## graphify

The knowledge graph lives in graphify-out/.
- When graphify-out/graph.json exists, run `graphify query "<question>"` before the first grep/search/source read for a
  codebase question (and put this rule in every code-exploring subagent prompt); then read only located lines. Use
  `graphify path "<A>" "<B>"` and `graphify explain "<concept>"`. Use graphify-out/wiki/index.md for broad navigation
  if it exists; GRAPH_REPORT.md only for broad architecture review.
- After modifying code, run `graphify update .` (AST-only, free).
- A large graph.json diff in a PR is expected; do not flag it.
