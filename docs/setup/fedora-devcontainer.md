# Fedora + Docker + VS Code Dev Containers

## Host requirements

Only install these on Fedora:

- Docker Engine + Docker Compose plugin
- Git
- VS Code
- VS Code extension: Dev Containers

.NET SDK and `dotnet-ef` are provided inside the Dev Container.

## Verify Docker on Fedora

```bash
id -nG
docker --version
docker compose version
docker info --format '{{.ServerVersion}}'
```

Your user must belong to the `docker` group and Docker commands must work without `sudo`.

## Open the project

1. Extract/clone the repository.
2. Open the repository folder in VS Code.
3. `Ctrl+Shift+P` -> **Dev Containers: Reopen in Container**.
4. Wait for `postCreateCommand` to finish.

The Dev Container starts two services:

- `app`: .NET 10 development environment.
- `db`: SQL Server 2022.

Inside the `app` container the database hostname is **`db`**, not `localhost`.

Default development connection string:

```text
Server=db,1433;Database=EnglishLearningDb;User Id=sa;Password=Change_this_password_123!;TrustServerCertificate=True;MultipleActiveResultSets=true
```

The default password is for local development only. Never use it for deployment.

## First commands inside the Dev Container

```bash
./scripts/dev-check.sh
```

Or run manually:

```bash
dotnet restore EnglishLearningPlatform.sln
dotnet build EnglishLearningPlatform.sln
dotnet test EnglishLearningPlatform.sln
dotnet run --project src/EnglishLearningPlatform.Web
```

Open `http://localhost:8080` on the Fedora host.

## First migration

Run only after the team agrees the initial model is ready:

```bash
dotnet ef migrations add InitialCreate \
  --project src/EnglishLearningPlatform.Infrastructure \
  --startup-project src/EnglishLearningPlatform.Web \
  --output-dir Persistence/Migrations

dotnet ef database update \
  --project src/EnglishLearningPlatform.Infrastructure \
  --startup-project src/EnglishLearningPlatform.Web
```

## Docker from inside the Dev Container

The Dev Container uses Docker-outside-of-Docker. Docker commands inside it talk to the Docker daemon on the Fedora host.

```bash
docker ps
docker compose version
```

Only use this with repositories you trust because access to the Docker socket is powerful.

## Reset the development database

From the Fedora host or from inside the Dev Container:

```bash
docker compose -f .devcontainer/docker-compose.yml down -v
```

Then reopen/rebuild the Dev Container.
