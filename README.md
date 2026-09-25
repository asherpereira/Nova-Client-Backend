# Nova Backend

Backend foundation for Nova, a free, modern communications platform.

This repository is the server-side implementation. The client lives separately in asherpereira/Nova-Client.

## Current status

Nova Backend 0.2 is an early but substantially expanded development foundation.

### Implemented
- ASP.NET Core 10 / .NET 10
- PostgreSQL persistence
- Account registration and password hashing
- JWT access authentication
- Short-lived access tokens
- Rotating refresh sessions
- Device/session tracking and revocation
- Authenticated profile endpoint
- Conversation and group-DM data model
- Conversation creation/listing
- Encrypted message persistence
- Client-generated message IDs for idempotent retries
- Paginated encrypted message history
- Membership checks before message access
- Realtime WebSocket authentication
- Realtime message fan-out to connected conversation members
- WebSocket reconnect-friendly event foundation
- Servers
- Server membership
- Server roles and permission bitmasks
- Role assignments
- Text and voice channel data model
- Server/channel creation foundation
- Development-safe additive schema bootstrap
- Basic API rate limiting
- Docker deployment

### Deliberately not production-complete yet
- Formal audited E2EE protocol
- Friend/request/block system
- Invites
- Full permission evaluation for every endpoint
- File/object storage
- Large-file transfer
- Push notification delivery
- WebRTC signaling/media infrastructure
- Voice/video/screen sharing
- Moderation/audit log system
- Bot/application API
- Server discovery
- Search/indexing
- Federation/self-hosting protocol
- Production reverse proxy/TLS configuration
- Full EF Core migration history

## Security model

Nova is designed so that private message plaintext is handled by clients rather than trusted to the backend.

The backend stores message ciphertext, nonce metadata and encryption-version information. The current backend does not define or claim a production E2EE protocol.

Do not invent cryptography in the client or server. The final Nova encryption protocol must use established, well-reviewed cryptographic designs/libraries and should be formally documented, tested, reviewed and independently audited before production use.

Access tokens are short-lived. Refresh tokens are stored server-side only as SHA-256 hashes and are rotated on refresh.

Never commit .env files, JWT keys, database passwords, private keys or other secrets.

## API foundation

Health: GET /health

Authentication: POST /api/v1/auth/register, POST /api/v1/auth/login, POST /api/v1/auth/refresh, POST /api/v1/auth/logout

Account/device: GET /api/v1/me, GET /api/v1/devices, POST /api/v1/devices, POST /api/v1/devices/{deviceId}/revoke

Conversations: POST /api/v1/conversations, GET /api/v1/conversations, GET /api/v1/conversations/{conversationId}/messages

Messages: POST /api/v1/messages

Servers: POST /api/v1/servers, GET /api/v1/servers, GET /api/v1/servers/{serverId}, POST /api/v1/servers/{serverId}/channels

Realtime: GET /ws

The first WebSocket message authenticates the connection with type=auth and an accessToken. After authentication the server sends a ready event. Persisted messages are delivered as message.created events to connected conversation members.

## Docker development deployment

Copy .env.example to .env, set strong random values for NOVA_DB_PASSWORD and NOVA_JWT_KEY, then run:

docker compose --env-file .env up -d --build

Check with:

docker compose ps
curl http://localhost:8080/health

If host port 8080 is occupied, set NOVA_HTTP_PORT=8180 in .env and use http://localhost:8180/health.

Do not expose the development HTTP endpoint directly to the public internet. Put Nova behind a properly configured TLS reverse proxy before public testing.

## Architecture direction

The backend is built around ASP.NET Core/.NET 10, PostgreSQL, REST APIs, WebSockets, WebRTC later for media, S3-compatible object storage later for attachments, Redis later where distributed presence/fan-out requires it, and Docker.

The server is intentionally separated from the client. The client is responsible for UI, local state, OS integration and eventual end-to-end encryption.

## Roadmap
1. Harden accounts, sessions and devices
2. Formalize the E2EE protocol
3. Finish DMs, group DMs and reliable realtime synchronization
4. Complete servers, roles, permissions, channels and invites
5. Add files and encrypted object storage
6. Add notifications and presence
7. Add WebRTC signaling/media infrastructure
8. Add moderation and audit logs
9. Add bots, integrations and webhooks
10. Add discovery/search
11. Add production migrations, observability and deployment automation
12. Support Windows, macOS, Android, iOS and web
13. Research mature self-hosting/federation options