# Nova Backend

Backend foundation for Nova, a free communications platform.

## Current status

Nova Backend 0.1 is an early development build.

Implemented:
- ASP.NET Core 10
- PostgreSQL persistence
- Account registration and login
- Password hashing
- JWT authentication
- Authenticated profile endpoint
- Encrypted-message storage model
- Authenticated WebSocket connection foundation
- Docker deployment

Not implemented yet:
- Friends
- Conversations
- Servers/channels
- Reliable message fan-out
- File uploads
- Voice/video
- Production E2EE protocol

## Security

Nova is being designed so that message plaintext is handled by clients. The backend stores opaque ciphertext, nonce metadata, and an encryption-version field.

The current encryption support is only a protocol foundation. It must not be considered production-grade end-to-end encryption until the Nova cryptographic protocol is formally designed, reviewed, tested, and audited.

Never commit .env files or production secrets.

## Run with Docker

1. Copy .env.example to .env.
2. Replace both placeholder values with long random secrets.
3. Run: docker compose --env-file .env up --build
4. Health check: http://localhost:8080/health

## API

Register: POST /api/v1/auth/register

Login: POST /api/v1/auth/login

Authenticated profile: GET /api/v1/me

Encrypted message storage: POST /api/v1/messages

WebSocket: ws://localhost:8080/ws

The first WebSocket message must authenticate the connection with type=auth and an accessToken.

## Roadmap

1. Accounts and authentication
2. DMs and conversations
3. Servers, channels and roles
4. Reliable realtime delivery
5. Client-side encryption protocol
6. Attachments and large-file handling
7. Voice/video
8. Moderation and administration
9. Windows, macOS, Android and iOS clients
10. Self-hosting and federation research
