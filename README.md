# Nova Backend 1.0.0

Production-oriented backend foundation for the Nova communications platform.

> Version 1.0.0 focuses on a reliable authenticated API, PostgreSQL persistence, realtime messaging, session/device security, and a clean boundary for future E2EE/media infrastructure.

## Quick start

1. Copy `.env.example` to `.env`.
2. Replace both placeholder secrets with strong random values.
3. Run `docker compose up -d --build`.
4. Check `GET /health` and `GET /health/ready`.
5. Put a TLS reverse proxy in front before exposing the service publicly.

See [docs/v1.0.0-release.md](docs/v1.0.0-release.md) for the release boundary and deployment requirements.

