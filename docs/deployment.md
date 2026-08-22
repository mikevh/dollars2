# Deployment

How Dollars2 is deployed to the home server: the compose stack's six services, configuration,
and the deploy path.

## Target host

Deployed to **claw**, reachable over Tailscale (`claw.tail303da.ts.net`). The stack is LAN/tailnet
only, never exposed to the public internet — that's why Elasticsearch runs single-node with
security disabled.

The `frontend` and `backend` services are HTTPS-only — no plain-HTTP fallback — for one reason:
passkeys (WebAuthn) require a secure context. This is not public-internet TLS — the cert is a
private-CA leaf issued for `claw.tail303da.ts.net`, signed by a CA that only devices on the
LAN/tailnet trust. The CA and its issued certs live outside this repo at `E:\ca` on the machine
that manages them; see `E:\ca\claw-cert-notes.md` there for how the cert was made and how to
install/renew it. `certs/claw.crt` and `certs/claw.key` (gitignored, like `.env`) must exist on
claw before `docker compose up` — without them neither service can start at all, since there is no
HTTP listener to fall back to.

## Services

`docker-compose.yml` defines six services:

| Service | Image | Purpose |
|--------|--------|--------|
| `backend` | built from `backend/Dollars2.Api` | the .NET API, https-only on `5063:8443` |
| `frontend` | built from `frontend` | the React app, https-only on `8443:443` |
| `elasticsearch` | `docker.elastic.co/elasticsearch/elasticsearch:9.0.0` | log sink for the backend |
| `kibana` | `docker.elastic.co/kibana/kibana:9.0.0` | browse/search logs, port `5601` |
| `dynamodb` | `amazon/dynamodb-local` | sync archive storage (raw provider payloads) |
| `dynamodb-admin` | `aaronshaf/dynamodb-admin` | browse/edit the sync archive table by hand, port `8001` |

Two named volumes persist state across container recreation: `esdata` (Elasticsearch indices) and
`dynamodata` (the sync archive).

`backend` reaches the log sink and archive over the compose network at `http://elasticsearch:9200`
and `http://dynamodb:8000` — internal addresses, not published to the host. `dynamodb-admin` talks
to `dynamodb` the same way.

`backend` waits on `elasticsearch` starting and `dynamodb` reporting healthy before it starts,
since the sync archive's table initializer runs once at startup — see the comments in
`docker-compose.yml` for why each `dynamodb` setting (`user: root`, the relative `-dbPath`, the
un-flagged healthcheck `curl`) is the way it is.

`dynamodb` has no AWS account behind it: `dynamodb-admin` and the backend both authenticate with
the same throwaway `local`/`local` credentials (hardcoded in `Program.cs` for the backend's own
client).

## Configuration

Copy `.env.example` to `.env` (gitignored, holds real values) before running compose. Two kinds
of value:

- **Secrets** — never committed, supplied only via `.env`: the JWT signing secret, the MSSQL
  connection string, and the Plaid client ID/secret.
- **Build-time** — baked into the frontend bundle at image build, not read at runtime:
  `VITE_API_BASE_URL`, the backend origin the built JS calls.

See `.env.example` for the full list of variables and their descriptions.

## Deploying

Normal path is the `deploy` skill (`.claude/skills/deploy/SKILL.md`), which runs
`scripts/deploy.sh`: pulls `master` on claw, rebuilds with `docker compose up -d --build`, and
verifies both the frontend and backend respond over the tailnet before declaring success.

Manual fallback, run on claw with `.env` already in place:

```bash
git pull
docker compose up -d --build
```

## Logs and backups

Backend logs ship to Elasticsearch (`Elasticsearch__Uri`) and are browsable in Kibana at
`http://claw.tail303da.ts.net:5601`; see `docs/backend.md`'s Logging section for the sink
configuration itself. Rolling log files also live inside the backend container at
`logs/dollars2-<date>.log`.

MSSQL is not part of this compose stack — it runs as a separate `mssql` container on claw, backed
up on its own schedule. See `docs/backups.md` for the full backup/restore runbook.

## Sync archive

`dynamodb` stores the raw JSON payloads returned by the bank sync providers (Plaid, SimpleFIN).
See `docs/sync_archive.md` for the key schema and versioning — not duplicated here.
