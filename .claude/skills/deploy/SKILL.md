---
name: deploy
description: Deploy Dollars2 to the home LAN server by SSHing in, pulling latest, and rebuilding the Docker containers. Use when the user asks to deploy, ship, or push out the current code to the home server.
---

# Deploy to home server

Deploys the current `master` branch to the home LAN server by SSH, using
`docker-compose.yml` at the repo root. The server already has Docker, a
clone of this repo, and a real `.env` file (created once, not touched by
git) with production secrets.

Server connection details:

- `<SSH_HOST>` — `claw.tail303da.ts.net` (Tailscale; LAN IP is `10.0.0.215`)
- `<SSH_USER>` — `m`
- `<REMOTE_REPO_PATH>` — `~/dollars2`

## Steps

1. SSH in and pull the latest code:
   ```
   ssh m@claw.tail303da.ts.net "cd ~/dollars2 && git pull"
   ```

2. Rebuild and restart the containers (reads `.env` in the same directory
   automatically):
   ```
   ssh m@claw.tail303da.ts.net "cd ~/dollars2 && docker compose up -d --build"
   ```

3. Confirm both containers are up and report their status back to the user:
   ```
   ssh m@claw.tail303da.ts.net "cd ~/dollars2 && docker compose ps"
   ```

4. **Verify the deployment actually serves traffic — this step is required,
   not optional.** Issue real HTTP requests to the frontend and backend and
   confirm both return `200`. Run these from the machine executing the deploy
   (i.e. NOT over `ssh` / `localhost`) so the check goes over the Tailscale
   network — the same path a user takes. A `docker compose ps` showing "Up"
   is not proof the app is reachable; containers can be up but not serving,
   or removed entirely by an earlier `docker compose down`.
   ```
   # Frontend (nginx serving the SPA) — expect 200
   curl -sS -m 10 -o /dev/null -w "frontend: %{http_code}\n" http://claw.tail303da.ts.net:8080/

   # Backend health endpoint (anonymous, side-effect free) — expect 200 and {"status":"healthy"}
   curl -sS -m 10 -w "\nbackend: %{http_code}\n" http://claw.tail303da.ts.net:5062/api/health
   ```
   Both must return `200` before you report the deploy as successful. If
   either fails to connect or returns a non-200, the deploy is NOT done:
   report the failure. Common causes — the containers aren't running (check
   `docker compose ps`; if empty, a prior `docker compose down` left them
   stopped — run `docker compose up -d`), the backend crashed on a bad `.env`
   value (check `docker logs dollars2-backend-1`), or the port isn't
   reachable over the tailnet.

5. Optionally clean up old dangling images so disk usage doesn't grow
   unbounded over repeated deploys:
   ```
   ssh m@claw.tail303da.ts.net "docker image prune -f"
   ```

If any step fails (SSH connection refused, git conflict, build error, or the
step 4 health checks), stop and report the exact error back to the user
rather than retrying blindly - don't force-push, hard-reset, or discard
changes on the server without asking first.
