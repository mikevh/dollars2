# Database Backup and Restore

How the Dollars2 MSSQL database is backed up on the home server, and the exact steps to restore it.

Set up 2026-08-06. Verified by restoring a cron-produced backup into a scratch database and
comparing row counts against the live database.

## What is being protected

MSSQL runs as a standalone `mssql` container on **claw** — it is *not* part of the `dollars2`
compose stack, so `docker compose down` in the app directory does not touch it.

| | |
|--------|--------|
| Container | `mssql`, `mcr.microsoft.com/mssql/server:2022-latest`, `MSSQL_PID=Express` |
| Restart policy | `unless-stopped` |
| Data | bind mount `/home/m/dockerdata/mssql` → `/var/opt/mssql` |
| Databases | `dollars2` (this app), `dollars`, `kidsavings` |
| Recovery model | `FULL` on all three |

Because the data directory is a **bind mount**, container restarts, recreation, and image upgrades
do not lose data. The `.mdf`/`.ldf` files live on claw's disk at
`/home/m/dockerdata/mssql/data/`. Backups exist to protect against everything else: a bad
migration, an accidental `DELETE`, or the disk failing.

The bind mount is not defined by any compose file — the container was created with a bare
`docker run`. If it is ever recreated, `-v /home/m/dockerdata/mssql:/var/opt/mssql` must be passed
again or SQL Server will come up with an empty data directory.

## Schedule and retention

| | |
|--------|--------|
| Full backup | 02:30 daily, all three databases |
| Log backup | Every hour at :15 |
| Local archive | `/home/m/backups/mssql`, gzipped, 14-day retention |
| Offsite | Synology NAS at 10.0.0.10 over sftp, **no retention limit** |
| Log file | `/var/log/mssql-backup.log` (root-readable only) |

Roughly 4.8 MB per 84 files at steady state; about 130 MB/year accumulating on the NAS. Local
pruning is by mtime, so a surviving `.trn` always has a full backup from the same night to restore
against.

### Why FULL recovery and hourly log backups

`FULL` recovery with hourly `BACKUP LOG` gives point-in-time restore to within the hour. The
tradeoff is that **the hourly job is load-bearing**: in `FULL` recovery the transaction log cannot
truncate until it is backed up, so if the hourly job silently stops, the `.ldf` grows without
bound until it fills the disk.

That failure mode is why the script has a `check` mode (see below). The alternative — `SIMPLE`
recovery with nightly fulls only — has fewer moving parts but loses up to a day.

Note that a database in `FULL` recovery behaves as `SIMPLE` until its *first* full backup: the log
chain is not armed and the log truncates at every checkpoint. This is why the 72 MB
`dollars2_log.ldf` seen before backups existed was a high-water mark from past migrations rather
than evidence of runaway growth.

## Components

| Path | What |
|--------|--------|
| `/usr/local/bin/mssql-backup.sh` | The job. `root:root`, mode `750` |
| `/etc/mssql-backup.conf` | Sourced by the script; holds `NAS_DEST`. Mode `600` |
| root crontab | `30 2 * * * … full` and `15 * * * * … log` |
| `/var/log/mssql-backup.log` | Append-only log of every run |
| `/home/m/backups/mssql` | Local gzipped archive |

`/etc/mssql-backup.conf` contains one line:

```
NAS_DEST="michael@10.0.0.10:/share/clawsql"
```

Any variable in the script's config block can be overridden there without editing the script.

### Why it runs as root

SQL Server writes backup files as uid `10001` with mode `640`, so the `m` account cannot read them
to gzip. The job must run from root's crontab. This also means a root SSH key
(`/root/.ssh/id_ed25519`, no passphrase) authenticates to the NAS — unavoidable for an unattended
job, since nobody is present at 02:30 to type a passphrase.

To read the log or the archive for diagnosis without root, use a throwaway container:

```bash
docker run --rm -v /var/log/mssql-backup.log:/l:ro alpine tail -20 /l
docker run --rm -v /home/m/backups/mssql:/a:ro alpine ls -la /a
```

## The script

Also useful as the source of truth if claw is ever rebuilt.

```bash
#!/usr/bin/env bash
#
# MSSQL backup for the `mssql` container on claw.
#   mssql-backup.sh full   -- nightly full backup of every database
#   mssql-backup.sh log    -- hourly transaction log backup
#
# Backups are written server-side into the container's bind mount, gzipped to
# $ARCHIVE, then pushed to the NAS over sftp. Runs as root: SQL Server writes the
# .bak files as uid 10001 with mode 640, which the `m` account cannot read.
#
# -E so the ERR trap fires for failures inside functions, not just at top level.
set -Eeuo pipefail

MODE=${1:-}
case "$MODE" in
  full|log|check) ;;
  *) echo "usage: $(basename "$0") full|log|check" >&2; exit 2 ;;
esac

# ---- config -----------------------------------------------------------------
CONTAINER=mssql
DBS=(dollars2 dollars kidsavings)
CDIR=/var/opt/mssql/backup                    # path inside the container
MOUNT=/home/m/dockerdata/mssql/backup         # same dir, host side of the bind mount
ARCHIVE=/home/m/backups/mssql                 # gzipped keep-dir on claw
LOGFILE=/var/log/mssql-backup.log
RETAIN_DAYS=14
TOOLS_IMAGE=mcr.microsoft.com/mssql-tools
NAS_DEST=""                                   # e.g. michael@10.0.0.10:/share/clawsql
LOG_STALE_HOURS=3                             # warn if newest .trn is older than this
LDF_WARN_MB=512                               # warn if any .ldf exceeds this

[ -f /etc/mssql-backup.conf ] && . /etc/mssql-backup.conf
# -----------------------------------------------------------------------------

exec 9>/var/lock/mssql-backup.lock
flock -n 9 || { echo "$(date -Is) [$MODE] another run holds the lock, skipping" >>"$LOGFILE"; exit 0; }

exec >>"$LOGFILE" 2>&1
say() { echo "$(date -Is) [$MODE] $*"; }
trap 'say "FAILED (line $LINENO)"' ERR

SA=$(docker inspect "$CONTAINER" --format '{{range .Config.Env}}{{println .}}{{end}}' \
     | grep -iE '^(MSSQL_)?SA_PASSWORD=' | head -1 | cut -d= -f2-)
if [ -z "$SA" ]; then
  say "could not read SA password from container env"
  exit 1
fi

sql() {
  docker run --rm --network "container:$CONTAINER" "$TOOLS_IMAGE" \
    /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "$SA" -C -b -h-1 -W -Q "$1"
}

install -d -o 10001 -g 10001 -m 750 "$MOUNT"
install -d -m 750 "$ARCHIVE"

stamp=$(date +%Y%m%d-%H%M%S)

# ---- staleness / size guard -------------------------------------------------
# The log chain is armed once a full backup exists; if the hourly job stops,
# the .ldf grows without bound. Shout about it in the log rather than silently.
run_check() {
  local newest age_h
  newest=$(find "$ARCHIVE" -name '*.trn.gz' -printf '%T@\n' 2>/dev/null | sort -n | tail -1)
  if [ -n "$newest" ]; then
    age_h=$(( ( $(date +%s) - ${newest%.*} ) / 3600 ))
    if [ "$age_h" -gt "$LOG_STALE_HOURS" ]; then
      say "WARNING: newest log backup is ${age_h}h old (threshold ${LOG_STALE_HOURS}h)"
    fi
  fi
  while read -r sz path; do
    if [ "$((sz / 1048576))" -gt "$LDF_WARN_MB" ]; then
      say "WARNING: $(basename "$path") is $((sz / 1048576))MB"
    fi
  done < <(find /home/m/dockerdata/mssql/data -name '*.ldf' -printf '%s %p\n' 2>/dev/null)
}

# ---- backup -----------------------------------------------------------------
MADE=()   # files produced by this run, pushed offsite at the end

backup_one() {
  local db=$1 ext=$2 verb=$3
  local cfile="$CDIR/${db}-${stamp}.${ext}"
  local hfile="$MOUNT/${db}-${stamp}.${ext}"
  local out="$ARCHIVE/${db}-${stamp}.${ext}.gz"

  sql "BACKUP $verb [$db] TO DISK='$cfile' WITH INIT, CHECKSUM;" >/dev/null
  sql "RESTORE VERIFYONLY FROM DISK='$cfile' WITH CHECKSUM;" >/dev/null

  gzip -c "$hfile" > "$out"
  rm -f "$hfile"
  MADE+=("$out")
  say "$(basename "$out") $(stat -c%s "$out") bytes"
}

# DSM's setuid rsync rejects `rsync --server` over ssh, so push over sftp
# instead: internal-sftp runs inside sshd and execs nothing on the NAS.
push_offsite() {
  local files=() f
  for f in "$@"; do
    if [ -n "$f" ]; then
      files+=("$f")
    fi
  done
  if [ ${#files[@]} -eq 0 ]; then
    say "nothing to push"
    return 0
  fi
  local host=${NAS_DEST%%:*} path=${NAS_DEST#*:}
  {
    echo "cd $path"
    printf 'put %s\n' "${files[@]}"
  } | sftp -q -o BatchMode=yes -o ConnectTimeout=30 -b - "$host"
  say "pushed ${#files[@]} file(s) to $NAS_DEST"
}

case "$MODE" in
  check)
    run_check
    ;;
  full)
    for db in "${DBS[@]}"; do
      backup_one "$db" bak DATABASE
    done
    run_check
    ;;
  log)
    for db in "${DBS[@]}"; do
      # Skip until a full backup exists, otherwise BACKUP LOG errors 4214.
      if ! compgen -G "$ARCHIVE/${db}-*.bak.gz" >/dev/null; then
        say "$db: no full backup yet, skipping log backup"
        continue
      fi
      backup_one "$db" trn LOG
    done
    ;;
esac

# ---- retention + offsite ----------------------------------------------------
# Nightly fulls mean any surviving .trn inside the window still has a full from
# the same night to restore from.
find "$ARCHIVE" -name '*.gz' -mtime +$RETAIN_DAYS -delete

# Hourly runs push only what they just made. `full` and `check` push the whole
# archive, so a night the NAS was unreachable heals on the next full run.
if [ -n "$NAS_DEST" ]; then
  case "$MODE" in
    log) push_offsite "${MADE[@]:-}" ;;
    *)   mapfile -t all < <(find "$ARCHIVE" -name '*.gz' | sort)
         push_offsite "${all[@]:-}" ;;
  esac
else
  say "NAS_DEST unset, offsite copy skipped"
fi

say "ok"
```

### Modes

| Mode | Does | Pushes offsite |
|--------|--------|--------|
| `full` | `BACKUP DATABASE` for every DB, then `check` | The entire archive |
| `log` | `BACKUP LOG` for every DB | Only the files it just made |
| `check` | Staleness + `.ldf` size warnings, no backup | The entire archive |

`full` and `check` push everything, so a night the NAS was unreachable heals on the next run.
`log` pushes only its three new files (~15 KB) to stay cheap at hourly frequency.

Integrity is checked twice: `WITH CHECKSUM` on write, and `RESTORE VERIFYONLY` immediately after.
Note that `VERIFYONLY` proves the file is structurally sound and its checksums match — it does
**not** prove the database restores. Only an actual restore does that.

Offsite push happens *last*, after backups are already written locally, so a NAS outage can never
cost a backup.

## Monitoring

```bash
sudo tail -20 /var/log/mssql-backup.log
```

A healthy hourly run looks like:

```
2026-08-06T21:15:02+00:00 [log] dollars2-20260806-211501.trn.gz 9571 bytes
2026-08-06T21:15:02+00:00 [log] dollars-20260806-211501.trn.gz 2589 bytes
2026-08-06T21:15:02+00:00 [log] kidsavings-20260806-211501.trn.gz 2586 bytes
2026-08-06T21:15:03+00:00 [log] pushed 3 file(s) to michael@10.0.0.10:/share/clawsql
2026-08-06T21:15:03+00:00 [log] ok
```

Every run ends in `ok` or `FAILED (line N)`. Grep for problems:

```bash
sudo grep -E 'FAILED|WARNING' /var/log/mssql-backup.log | tail
```

`WARNING: newest log backup is Nh old` means the hourly job has stopped — investigate immediately,
because the transaction log is now growing unbounded.

## Restore procedures

All of these use a `sq` helper for SQL and a staging step to get a gzipped backup into a path
SQL Server can read. Run on claw:

```bash
SA=$(docker inspect mssql --format '{{range .Config.Env}}{{println .}}{{end}}' \
     | grep -iE '^(MSSQL_)?SA_PASSWORD=' | head -1 | cut -d= -f2-)
sq() { docker run --rm --network container:mssql mcr.microsoft.com/mssql-tools \
         /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "$SA" -C -b -Q "$1"; }
```

Staging decompresses an archive file into the container's backup directory. It runs in a container
because the archive is root-owned:

```bash
stage() {   # stage <file.bak.gz> <target-name.bak>
  docker run --rm -v /home/m/backups/mssql:/a:ro -v /home/m/dockerdata/mssql/backup:/b alpine \
    sh -c "gunzip -c /a/$1 > /b/$2 && chown 10001:10001 /b/$2"
}
```

Files are then visible to SQL Server at `/var/opt/mssql/backup/<name>`.

### A. Verify a backup restores (non-destructive)

Run this periodically. It touches nothing live.

```bash
stage dollars2-20260806-023001.bak.gz restoretest.bak

# Logical file names inside the backup
sq "RESTORE FILELISTONLY FROM DISK='/var/opt/mssql/backup/restoretest.bak';"

sq "RESTORE DATABASE [dollars2_restoretest]
    FROM DISK='/var/opt/mssql/backup/restoretest.bak'
    WITH MOVE 'dollars2'     TO '/var/opt/mssql/data/dollars2_restoretest.mdf',
         MOVE 'dollars2_log' TO '/var/opt/mssql/data/dollars2_restoretest_log.ldf',
         RECOVERY;"

# Compare against live — differences should be exactly the activity since the backup
sq "SET NOCOUNT ON; SELECT t.name, SUM(p.rows) FROM dollars2_restoretest.sys.tables t
    JOIN dollars2_restoretest.sys.partitions p
      ON p.object_id = t.object_id AND p.index_id IN (0,1)
    GROUP BY t.name ORDER BY t.name;"

# Clean up
sq "DROP DATABASE [dollars2_restoretest];"
docker exec -u root mssql rm -f /var/opt/mssql/backup/restoretest.bak
```

`MOVE` is mandatory — without it the restore tries to write to the live database's `.mdf`/`.ldf`
paths and fails (or worse, succeeds against the wrong files).

### B. Restore the live database to the last full backup

**Destructive.** Overwrites `dollars2`. Stop the backend first so nothing reconnects mid-restore.

```bash
docker stop dollars2-backend-1

stage dollars2-20260806-023001.bak.gz restore.bak

sq "ALTER DATABASE [dollars2] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;"
sq "RESTORE DATABASE [dollars2] FROM DISK='/var/opt/mssql/backup/restore.bak'
    WITH REPLACE, RECOVERY;"
sq "ALTER DATABASE [dollars2] SET MULTI_USER;"

docker start dollars2-backend-1
docker exec -u root mssql rm -f /var/opt/mssql/backup/restore.bak
```

`SINGLE_USER WITH ROLLBACK IMMEDIATE` kicks off existing connections; without it the restore fails
with "database is in use". `WITH REPLACE` is required to overwrite an existing database.

Everything since 02:30 is lost by this procedure. Use C instead unless the log chain is broken.

### C. Point-in-time restore (full + log chain)

Recovers to any moment covered by the log backups — up to the last `:15`.

Restore the full backup **`WITH NORECOVERY`**, leaving the database able to accept more log files,
then apply every `.trn` taken after it in chronological order:

```bash
docker stop dollars2-backend-1

stage dollars2-20260806-023001.bak.gz restore.bak
sq "ALTER DATABASE [dollars2] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;"
sq "RESTORE DATABASE [dollars2] FROM DISK='/var/opt/mssql/backup/restore.bak'
    WITH REPLACE, NORECOVERY;"

# Every dollars2 .trn newer than the full, in filename order
docker run --rm -v /home/m/backups/mssql:/a:ro alpine \
  sh -c "ls -1 /a/dollars2-*.trn.gz | sort" | while read -r f; do echo "${f#/a/}"; done
```

For each of those, in order:

```bash
stage dollars2-20260806-031501.trn.gz apply.trn
sq "RESTORE LOG [dollars2] FROM DISK='/var/opt/mssql/backup/apply.trn' WITH NORECOVERY;"
docker exec -u root mssql rm -f /var/opt/mssql/backup/apply.trn
```

On the **last** file, either take everything in it:

```bash
sq "RESTORE LOG [dollars2] FROM DISK='/var/opt/mssql/backup/apply.trn' WITH RECOVERY;"
```

or stop at a precise moment — the statement *before* the damage:

```bash
sq "RESTORE LOG [dollars2] FROM DISK='/var/opt/mssql/backup/apply.trn'
    WITH STOPAT = '2026-08-06T14:22:00', RECOVERY;"
```

Then:

```bash
sq "ALTER DATABASE [dollars2] SET MULTI_USER;"
docker start dollars2-backend-1
```

The database is unusable until a `RESTORE … WITH RECOVERY` completes the sequence. If you apply
the last log with `NORECOVERY` by mistake, finish with
`sq "RESTORE DATABASE [dollars2] WITH RECOVERY;"`.

Filename timestamps are the backup's start time in **UTC**, matching the log file.

### D. Restore from the NAS copy

Only needed if claw's disk is gone. The NAS holds the same gzipped files at
`/volume1/share/clawsql` (or `/share/clawsql` over sftp — DSM chroots sftp sessions, so the two
paths refer to the same folder).

```bash
sftp michael@10.0.0.10:/share/clawsql
sftp> get dollars2-20260806-023001.bak.gz
```

Then follow procedure A, B, or C with the retrieved file.

### E. Rebuilding the instance from scratch

If the container is lost but `/home/m/dockerdata/mssql` survives, just recreate it with the same
bind mount and the databases come back attached — no restore needed:

```bash
docker run -d --name mssql --restart unless-stopped \
  -e ACCEPT_EULA=Y -e MSSQL_PID=Express -e MSSQL_SA_PASSWORD='<password>' \
  -p 1433:1433 -v /home/m/dockerdata/mssql:/var/opt/mssql \
  mcr.microsoft.com/mssql/server:2022-latest
```

If the data directory is *also* gone, create the container, then restore each database with
procedure B. Recreate the databases first if the restore complains they do not exist.

## Gotchas

Notes on things that cost real time when this was built.

**`sqlcmd` is not in the SQL Server 2022 image.** Microsoft removed `mssql-tools` from it. There is
also no `mssql-tools18` image on MCR. Use `mcr.microsoft.com/mssql-tools`, where the binary is at
`/opt/mssql-tools/bin/sqlcmd`, run as a sidecar sharing the container's network namespace
(`--network container:mssql`) so `-S localhost` works. It needs `-C` to trust the self-signed cert.

**`STATS=0` is invalid** in `BACKUP` — the parameter accepts 1–100. Omit it entirely.

**Backup compression is unavailable.** `MSSQL_PID=Express` does not support `WITH COMPRESSION`;
that is why the script gzips on the host instead. Express also has no SQL Server Agent, which is
why scheduling is host cron.

**Use `WITH COPY_ONLY` for ad-hoc test backups.** A normal full backup arms the log chain and a
normal log backup truncates the log — either can break the restore chain if the file is then
discarded. Copy-only backups do neither.

**The SA password never needs storing on disk.** It is readable from the container environment via
`docker inspect`, which is what the script does at runtime.

**Synology DSM blocks rsync-over-SSH.** `/usr/bin/rsync` on DSM is setuid root and rejects
`rsync --server`, returning `Permission denied, please try again.` on the remote's *stderr* after
the exec is accepted — which looks exactly like an SSH auth failure but is not. This was never
root-caused; sftp was used instead, which runs inside `sshd` via `internal-sftp` and execs nothing.

**SFTP is a separate DSM toggle** from SSH: Control Panel → File Services → FTP → SFTP. Without it,
sftp fails with `Connection closed`.

**DSM chroots sftp sessions.** Shares appear at the root, so the destination is `/share/clawsql`
over sftp but `/volume1/share/clawsql` over plain SSH.

**`set -e` alone does not fire an `ERR` trap inside a function** — `set -E` is required. Without it
a failing offsite push exits non-zero while logging nothing, which is the worst possible outcome
for a backup job.

## Known gaps

- **NAS retention is unbounded.** Nothing prunes the offsite copy. Deliberate: deletion logic
  running against the only offsite copy is a worse risk than ~130 MB/year.
- **Failures are logged, not alerted.** Nothing emails or pings on `FAILED`. Discovering a broken
  job depends on reading the log. A healthchecks.io ping or similar dead-man's switch would close
  this.
- **Backups have never been tested across a claw reboot.** Cron is enabled and the container is
  `unless-stopped`, so it should survive, but this is reasoning rather than evidence.
- **claw's IP is a DHCP lease** (`10.0.0.215`). Nothing depends on it today, but adding a
  `from="10.0.0.215"` restriction to the NAS `authorized_keys` would break when the lease moves,
  unless a reservation is set first.
