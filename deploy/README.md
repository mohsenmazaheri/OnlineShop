# Deploying a shop

Each shop is one copy of the stack in `docker-compose.yml`: PostgreSQL 18, the API and the web app.
The same two images serve every shop; everything shop-specific lives in `deploy/.env`. Works on Linux and Windows
hosts with Docker. (systemd / IIS guides without Docker follow in roadmap phase 12.)

## First start

```bash
cp deploy/.env.example deploy/.env      # fill in every value; keep this file private
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build
```

- The API applies database migrations and creates the roles and the first admin account on start.
- Only the web container is published (`WEB_PORT`, default 3000). Put a TLS reverse proxy in front of it;
  `nginx.conf` is an example (one canonical host, HTTP → HTTPS).
- Health: `docker compose ps` (web has a healthcheck); the API answers `/health/live` and `/health/ready` inside the network.

## Test vs production instances

| Setting | Test / staging | Production |
|---|---|---|
| `SEO_ALLOW_INDEXING` | `false` (noindex, robots `Disallow: /`) | `true` |
| `SITE_URL` | may be `http://` | `https://…` (enables HSTS) |

## Search engine (optional)

PostgreSQL full-text search is the default and is enough for a few thousand products. For a large catalogue:

```bash
# deploy/.env: SEARCH_PROVIDER=OpenSearch (the bundled container is http://search:9200)
docker compose -f deploy/docker-compose.yml --env-file deploy/.env --profile search up -d
```

- The engine only holds a copy: the API fills it from PostgreSQL in the background (one index per language) and the
  admin's "rebuild index" (SEO page) rebuilds it into new indexes and switches over without downtime. Prices and
  stock are always read from PostgreSQL.
- When the engine is down, slow or more than five minutes behind, the database answers searches and a warning is
  logged; nothing else is affected. The search-data volume can be deleted at any time and is rebuilt on start.
- An existing cluster works too: `SEARCH_PROVIDER=Elasticsearch` (or OpenSearch), `SEARCH_ENGINE_URL` and its
  credentials (`SEARCH_ENGINE_USERNAME`/`PASSWORD` or an Elasticsearch `SEARCH_ENGINE_API_KEY`). Give every shop its
  own `SEARCH_INDEX_PREFIX`. Elasticsearch's licence (Elastic License / SSPL / AGPL) is the operator's responsibility.
- Never publish the bundled engine's port: its security plugin is off because only the API can reach it.

## Images and Iran

CI builds `onlineshop-api` and `onlineshop-web` and publishes them to GitHub Container Registry on every push to `main`.
Docker Hub and some registries are restricted from Iran: for an Iran-hosted shop, mirror the images to a registry that
is reachable there and set `REGISTRY` / `IMAGE_TAG` in `.env` instead of building on the server. The same goes for the
optional search engine (`SEARCH_IMAGE`); Elastic blocks downloads from Iran, so use OpenSearch there.
