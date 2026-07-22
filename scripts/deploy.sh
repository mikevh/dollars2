#!/usr/bin/env bash
#
# Deploy Dollars2 to the home LAN server.
#
# Runs on macOS/Linux and on Windows via Git Bash. Requires `ssh` and `curl`
# on PATH and key-based SSH access to the server (no password prompt).
#
# Steps: pull latest master on the server, rebuild+restart the Docker
# containers (stamping the frontend with the deployed commit), confirm the
# containers are up, then verify the frontend and backend actually serve HTTP
# 200 over the Tailscale network. Exits non-zero on any failure.
#
# Config can be overridden via environment variables, e.g.:
#   SSH_HOST=10.0.0.215 ./scripts/deploy.sh
#
set -euo pipefail

SSH_HOST="${SSH_HOST:-claw.tail303da.ts.net}"
SSH_USER="${SSH_USER:-m}"
REMOTE_REPO_PATH="${REMOTE_REPO_PATH:-~/dollars2}"
FRONTEND_URL="${FRONTEND_URL:-http://${SSH_HOST}:8080/}"
BACKEND_HEALTH_URL="${BACKEND_HEALTH_URL:-http://${SSH_HOST}:5062/api/health}"

SSH_TARGET="${SSH_USER}@${SSH_HOST}"

echo "==> [1/5] Pulling latest code on ${SSH_TARGET}"
ssh "$SSH_TARGET" "cd $REMOTE_REPO_PATH && git pull"

echo "==> [2/5] Building and (re)starting containers"
# The \$(...) is escaped so the command substitution runs on the server,
# against its just-pulled checkout, not on this machine.
ssh "$SSH_TARGET" "cd $REMOTE_REPO_PATH && VITE_BUILD_ID=\$(git rev-parse --short HEAD) docker compose up -d --build"

echo "==> [3/5] Container status"
ssh "$SSH_TARGET" "cd $REMOTE_REPO_PATH && docker compose ps"

echo "==> [4/5] Verifying HTTP endpoints over the tailnet"
frontend_code="$(curl -sS -m 10 -o /dev/null -w "%{http_code}" "$FRONTEND_URL" || echo "000")"
echo "frontend (${FRONTEND_URL}): ${frontend_code}"

# The backend needs a few seconds to boot after its container starts (Kestrel
# startup + the on-launch bank sync), while `docker compose up -d` returns as
# soon as the container is started. Poll instead of checking once so a cold
# backend doesn't fail the gate on a deploy that actually succeeded.
backend_code="000"
backend_body=""
for attempt in $(seq 1 15); do
  backend_response="$(curl -sS -m 10 -w $'\n%{http_code}' "$BACKEND_HEALTH_URL" || printf '\n000')"
  backend_code="$(printf '%s' "$backend_response" | tail -n1)"
  backend_body="$(printf '%s' "$backend_response" | sed '$d')"
  if [ "$backend_code" = "200" ]; then
    break
  fi
  echo "backend not ready (attempt ${attempt}/15, code ${backend_code}) — retrying in 2s"
  sleep 2
done
echo "backend  (${BACKEND_HEALTH_URL}): ${backend_code} ${backend_body}"

if [ "$frontend_code" != "200" ] || [ "$backend_code" != "200" ]; then
  echo "ERROR: health check failed (frontend=${frontend_code} backend=${backend_code}) — deploy NOT complete" >&2
  echo "Investigate: ssh ${SSH_TARGET} 'cd ${REMOTE_REPO_PATH} && docker compose ps' and 'docker logs dollars2-backend-1'" >&2
  exit 1
fi

echo "==> [5/5] Pruning dangling images"
ssh "$SSH_TARGET" "docker image prune -f" || echo "warning: image prune failed (non-fatal)"

echo "Deploy successful."
