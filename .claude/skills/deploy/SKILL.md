---
name: deploy
description: Deploy Dollars2 to the home LAN server by SSHing in, pulling latest, and rebuilding the Docker containers. Use when the user asks to deploy, ship, or push out the current code to the home server.
---

# Deploy to home server

Deploys the current `main` branch to the home LAN server by SSH, using
`docker-compose.yml` at the repo root. The server already has Docker, a
clone of this repo, and a real `.env` file (created once, not touched by
git) with production secrets.

Fill in these placeholders before first use:

- `<SSH_HOST>` — SSH alias or Tailscale hostname/IP of the home server
- `<SSH_USER>` — SSH user on that server
- `<REMOTE_REPO_PATH>` — path to the repo checkout on that server, e.g. `~/dollars2`

## Steps

1. SSH in and pull the latest code:
   ```
   ssh <SSH_USER>@<SSH_HOST> "cd <REMOTE_REPO_PATH> && git pull"
   ```

2. Rebuild and restart the containers (reads `.env` in the same directory
   automatically):
   ```
   ssh <SSH_USER>@<SSH_HOST> "cd <REMOTE_REPO_PATH> && docker compose up -d --build"
   ```

3. Confirm both containers are up and report their status back to the user:
   ```
   ssh <SSH_USER>@<SSH_HOST> "cd <REMOTE_REPO_PATH> && docker compose ps"
   ```

4. Optionally clean up old dangling images so disk usage doesn't grow
   unbounded over repeated deploys:
   ```
   ssh <SSH_USER>@<SSH_HOST> "docker image prune -f"
   ```

If any step fails (SSH connection refused, git conflict, build error), stop
and report the exact error back to the user rather than retrying blindly -
don't force-push, hard-reset, or discard changes on the server without
asking first.
