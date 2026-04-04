# Start the Application

## Configuration

Edit `.env` in the project root to configure API keys and settings.

## Commands

```bash
# Start everything (backend, frontend, qdrant, postgres)
docker compose up -d

# Rebuild and start (after code changes)
docker compose up -d --build

# Full rebuild (clear cache issues)
docker compose build --no-cache && docker compose up -d --force-recreate

# Check status
docker compose ps

# View logs
docker compose logs -f

# View specific service logs
docker compose logs -f backend

# Stop everything
docker compose down

# Stop and remove volumes (fresh database)
docker compose down -v
```

## Access

- Frontend: http://localhost:5173
- Backend API: http://localhost:8080
- Qdrant Dashboard: http://localhost:6333/dashboard
- Postgres: localhost:5432

## Default Login

- Username: `admin`
- Password: `admin123`
