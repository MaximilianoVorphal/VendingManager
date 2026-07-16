# Changelog

All notable changes to VendingManager will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- F0: Automated MSSQL backups with retention (14d) and off-host SCP transfer
- F5: Transferencia comprobantes migrated to DB varbinary — zero filesystem state
- F1: Production stack (`docker-compose.yml`) with backup bind mount and 127.0.0.1 DB binding
- F3: Fail-fast EF migrations — app exits on migration failure instead of running with mismatched schema
- F4: Docker healthchecks, `depends_on: service_healthy`, log rotation (10MB/3 files), restart policies
- F2: Tag-triggered deploys (`v*`) — pushes to master no longer touch production
- Restore runbook (`docs/runbook-restore.md`) and cutover runbook (`docs/runbook-cutover-produccion.md`)

### Changed
- CI deploys via `docker-compose.yml` instead of `docker-compose.dev.yml`
- CI deploy condition: `refs/tags/v*` instead of `refs/heads/master`
- Deploy script uses `git checkout ${GITHUB_REF_NAME}` (tag) instead of `git pull origin master`

### Removed
- `IUploadPathProvider` / `DefaultUploadPathProvider` — last filesystem state eliminated
- `VendingConfig.FacturaUploadPath` and compose uploads bind mounts
- `Transferencia.ComprobanteImagenPath` column (replaced by varbinary)
- `Devolucion.ComprobanteImagenPath` (dead field)

### Security
- Path traversal guard in backfill endpoints (`..` rejected + `Path.GetFullPath` containment)
- File signature validation in backfill endpoints (`FileSignatureValidator`)

## [1.0.0] — YYYY-MM-DD
### Infrastructure Hardening Release
First tagged release after roadmap-fortalecimiento-infra (F0-F5). See Unreleased above for full changes.
