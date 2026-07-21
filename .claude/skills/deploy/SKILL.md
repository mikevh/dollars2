---
name: deploy
description: Deploy Dollars2 to the home LAN server by SSHing in, pulling latest, and rebuilding the Docker containers. Use when the user asks to deploy, ship, or push out the current code to the home server.
---

# Deploy to home server

Deploys the current `master` branch to the home LAN server. The actual work
lives in `scripts/deploy.sh` at the repo root — a self-contained bash script
that is the single source of truth for the deploy steps. This skill just runs
it and interprets the result.

The server already has Docker, a clone of this repo, and a real `.env` file
(created once, not touched by git) with production secrets.

## Steps

1. Run the deploy script from the repo root (works in Git Bash, WSL, macOS,
   or Linux):
   ```
   ./scripts/deploy.sh
   ```

   The script performs, in order:
   - `git pull` on the server (`m@claw.tail303da.ts.net:~/dollars2`)
   - `docker compose up -d --build`, stamping `VITE_BUILD_ID` with the
     server's just-pulled short commit hash so the UI footer shows the
     running build
   - `docker compose ps` to confirm both containers are up
   - **Required health verification** — real HTTP requests over the Tailscale
     network to the frontend (`http://claw.tail303da.ts.net:8080/`) and the
     backend health endpoint (`http://claw.tail303da.ts.net:5062/api/health`),
     both of which must return `200`. The script exits non-zero if either
     fails.
   - `docker image prune -f` to reclaim dangling-image disk space

   Config (host, user, repo path, URLs) is overridable via environment
   variables — see the header of `scripts/deploy.sh`.

2. Report the outcome to the user. A `Deploy successful.` line with both
   endpoints at `200` means it's done. Include the deployed commit hash.

## If it fails

The script exits non-zero and prints the failing step. Stop and report the
exact error rather than retrying blindly — do **not** force-push, hard-reset,
or discard changes on the server without asking first. Common causes:

- **`npm ci` / build error** — usually the code on `master` is broken (e.g. a
  `package-lock.json` out of sync). Fix it in the repo, push, and re-run.
- **Health check returns non-200 or fails to connect** — the containers may
  not be running (`ssh m@claw.tail303da.ts.net "cd ~/dollars2 && docker compose ps"`;
  if empty, a prior `docker compose down` left them stopped — `docker compose up -d`),
  the backend may have crashed on a bad `.env` value
  (`docker logs dollars2-backend-1`), or the port isn't reachable over the
  tailnet.
- **SSH connection refused** — check Tailscale is up on both ends.
