# StatisticService

Part of the **6-sem-musik-quiz** system. ASP.NET Core (.NET 10) Web API using MVC controllers and a layered architecture.

## Architecture

```
Api  ──►  Service  ──►  Repository
  └─────────┴──────────────┴──►  Domain
```

| Project | Responsibility |
|---|---|
| `src/StatisticService.Api` | Controllers, middleware, `Program.cs`. Thin: validate input, call a service, return HTTP result. |
| `src/StatisticService.Service` | Business logic. Talks to repositories through interfaces, returns DTOs. |
| `src/StatisticService.Repository` | Data access with EF Core (PostgreSQL). The only layer that knows about the database. |
| `src/StatisticService.Domain` | Entities and DTOs. No dependencies. |
| `tests/StatisticService.Service.Tests` | Unit tests for services (xUnit + NSubstitute). |
| `tests/StatisticService.Api.Tests` | Integration tests using `WebApplicationFactory`. |

`Example*` files are placeholders showing the pattern end to end. Replace them with real entities.

### Adding a new feature

1. Entity in `Domain/Entities`, request/response DTOs in `Domain/DTOs`
2. EF configuration in `Repository/Data/Configurations`, `DbSet` in `AppDbContext`
3. `I<Name>Repository` + implementation, register in `RepositoryLayerExtensions`
4. `I<Name>Service` + implementation + mappings, register in `ServiceLayerExtensions`
5. Controller in `Api/Controllers`
6. Tests

## API

Publicly reachable through the edge as `/api/statisticservice/...`, e.g.
`GET /api/statisticservice/api/statistics/weekly`.

### `GET /api/statistics/weekly?week=current`

The weekly leaderboard for the front page. Needs a login (`Authorization: Bearer <JWT>` from
UserService); any role may call it. `week` is `current` (also used when the parameter is left out)
or an ISO 8601 week such as `2026-W47`.

| Status | When |
|---|---|
| 200 | `{ weekStart, weekEnd, mostWins: [{ userId, userName, wins, gamesPlayed }], winRate: [{ userId, userName, winRate, gamesPlayed }], accuracy: [{ userId, userName, accuracy, gamesPlayed }] }` |
| 400 | `week` is empty, malformed or not a real week (e.g. `2026-W54`, or `2025-W53`, since 2025 has 52 weeks). ProblemDetails body. |
| 401 | Missing, expired or invalid token. |

Definitions:

- **The week** runs Monday 00:00:00 to Sunday 23:59:59 in Danish time (`Europe/Copenhagen`, with daylight saving), judged on the game's `EndTime`. The query is the half-open UTC interval `[Monday 00:00, next Monday 00:00)`, so a week is 167 or 169 hours long when the clocks change. `weekStart`/`weekEnd` carry the Danish UTC offset of that moment (so in the October week they are `+02:00` and `+01:00`). The calculation is the pure function `WeeklyPeriod.TryParse`.
- **Win**: `Placement = 1`. A shared first place counts as a win for everyone with placement 1.
- **winRate** = wins / games played in the week. **accuracy** = sum(`CorrectAnswers`) / sum(`AnsweredRounds`), `0` if nothing was answered. Both are **fractions from 0 to 1** (`0.75` = 75 %); the frontend formats them.
- `winRate` and `accuracy` need **at least 5 games** in the week. `mostWins` has no minimum, but players with 0 wins are not listed.
- Each list is the top 10, highest first. Ties: most games, then user name (case-insensitive, ordinal), then user id. A renamed player is shown with the name from their latest game that week.

### `GET /api/statistics/me`

My own stats. Needs a JWT from UserService (see Authentication settings); the user id is the `sub` claim.
401 without a valid token, or if `sub` is not a number.

```json
{
  "gamesPlayed": 3,
  "wins": 2,
  "winRate": 0.6667,
  "accuracy": 0.5,
  "roundsPlayed": 30,
  "timePlayedSeconds": 2100,
  "mostPlayedCategory": { "categoryId": 7, "categoryName": "Rock", "games": 2 },
  "mostCommonFriend": { "userId": 2, "userName": "Alice", "gamesTogether": 2 }
}
```

- `wins` = games with `Placement = 1`. `winRate` = wins / gamesPlayed, `accuracy` = sum(CorrectAnswers) / sum(AnsweredRounds). Both are fractions 0-1, and 0 when there is nothing to divide by.
- `roundsPlayed` = sum of `Rounds`; `timePlayedSeconds` = sum of `EndTime - StartTime` in whole seconds.
- `mostPlayedCategory` / `mostCommonFriend` are `null` when there are no games (or no other players). A tie goes to the category / friend with the newest game; the friend's name is taken from that game.
- No games yet: 200 with zeros and `null`s.

### Authentication settings

UserService issues the tokens (HS256); this service only validates them, so the values must match UserService.

| Setting | Env var | Default |
|---|---|---|
| `Jwt:SigningKey` | `Jwt__SigningKey` | none. **Secret**, never commit it. Without it every token gets 401 (the service still starts, `/health` is anonymous). |
| `Jwt:Issuer` | `Jwt__Issuer` | `slopster-userservice` |
| `Jwt:Audience` | `Jwt__Audience` | `slopster` |

The Issuer/Audience defaults are copied from the other services' plans, not from a verified Platform contract. Check them against UserService before relying on them.

### Contract status

The `statisticservice.api` contract for this endpoint has not been added to Platform yet, and the
StatisticService database contract `0.2a3436aa4dd8` predates `UserName`, `Score`, `CorrectAnswers` and `AnsweredRounds`
(added in SCRUM-105). Both need a separate Platform PR before the contract can be called verified.

## Game results from RabbitMQ (SCRUM-85)

`ResultConsumer` (a hosted service) reads the `GameFinished` messages GameService publishes when a
game ends and saves them as `GameStats` + `UserStats`.

- Declares the durable queue `statisticservice.game-finished` and binds it to the topic exchange
  `game-events` with routing key `game.finished.v1`. All pods share the queue, so each message is
  saved once. Messages published while the queue did not exist yet were dropped by RabbitMQ.
- Message: camelCase JSON `{ gameId, rounds, startTime, endTime, catId, categoryName, players: [{ userId, userName, score, correctAnswers, answeredRounds, placement }] }`,
  as described in SCRUM-83 (the Platform contract file is not merged yet).
- **Idempotent**: a `gameId` that is already saved is acked and ignored.
- **Ack** after saving. **Nack without requeue** (dropped, no dead-letter queue) when the JSON can't be read
  or breaks a rule: missing `gameId`, `rounds` ≤ 0, `endTime` before `startTime`, empty or > 60 char
  category, no players, the same `userId` twice, empty or > 20 char `userName`, `correctAnswers` > `answeredRounds`,
  `placement` < 1. **Nack with requeue** (after `ReconnectDelaySeconds`) when saving fails, e.g. the database is down.
- If RabbitMQ is unreachable the API keeps running and the consumer reconnects every `ReconnectDelaySeconds`.

| Setting | Env var | Default |
|---|---|---|
| `RabbitMQ:Enabled` | `RabbitMQ__Enabled` | `true`; `false` in `appsettings.Development.json`, so local runs and tests never take messages from the real queue |
| `RabbitMQ:Host` / `Port` / `VirtualHost` | `RabbitMQ__Host` ... | `localhost` / `5672` / `/` (cluster: set by `statisticservice-secrets` in gitops) |
| `RabbitMQ:Username` / `Password` | `RabbitMQ__Username` ... | `guest` / `guest`. **Secret** in the cluster (OpenBao `kv/quiz/rabbitmq`). |
| `RabbitMQ:ExchangeName` / `RoutingKey` / `QueueName` | | `game-events` / `game.finished.v1` / `statisticservice.game-finished` |
| `RabbitMQ:PrefetchCount` / `ReconnectDelaySeconds` | | `10` / `5` |

## Running locally

Locally the API always runs against the **real** Postgres in the cluster
(`quiz-postgres.tail5a1bc5.ts.net`, Tailscale on). **This is live data.**
Migrations are turned off locally (`Database:MigrateOnStartup=false`), so a branch can never
change the shared schema; schema changes reach the database only through a deploy from `main`.

The password is a secret, so it is not in Git. Get it with
`bao kv get -field=statisticservice kv/quiz/postgres` (or ask Asger).

**Docker:** keep the password in a local `.env` file (in `.gitignore`). Compose stops with a
clear message if it is missing.

```powershell
Copy-Item .env.example .env      # once, then fill in DB_PASSWORD
docker compose up --build
```

**From your IDE:** `appsettings.Development.json` has the host and user but no password;
Npgsql reads it from `PGPASSWORD`.

```powershell
$env:PGPASSWORD = "<password>"
dotnet run --project src/StatisticService.Api
```

Without Tailscale: `kubectl -n quiz port-forward svc/postgresql 15432:5432`, then set
`DB_HOST=host.docker.internal` and `DB_PORT=15432` in `.env` (see `.env.example`).

- API: http://localhost:5003
- Swagger: http://localhost:5003/swagger
- Health: http://localhost:5003/health
- Ready (also checks Postgres): http://localhost:5003/health/ready

## Database migrations

The API applies pending migrations when it starts, so a deploy from `main` updates the schema by itself. EF skips migrations already listed in `__EFMigrationsHistory`, and locks the database while migrating, so several pods starting at once is fine. Turn it off with `Database__MigrateOnStartup=false`.

To add a migration:

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add <Name> --project src/StatisticService.Repository --startup-project src/StatisticService.Api
```

Do **not** run `dotnet ef database update` locally: your connection string points at the real
database, so it would apply your branch's migrations to the shared schema. Merge to `main`
and the deploy applies them.

## Tests

```bash
dotnet test
```

## Service ports

| Service | API |
|---|---|
| UserService | 5001 |
| MusicService | 5002 |
| StatisticService | 5003 |
| LobbyService | 5004 |
| GameService | 5005 |
