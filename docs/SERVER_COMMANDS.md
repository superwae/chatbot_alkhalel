# Server Commands Reference

> Complete reference for managing the Municipality Chatbot on the production server

## Table of Contents
- [SSH Access](#ssh-access)
- [Server Network Configuration](#server-network-configuration)
- [Check Status](#check-status)
- [View Logs](#view-logs)
- [Deploy Updates](#deploy-updates)
- [Environment Variables](#environment-variables)
- [Database Operations](#database-operations)
- [Full Database Cleanup](#full-database-cleanup)
- [Troubleshooting](#troubleshooting)
- [Local Development](#local-development)

---

## SSH Access

```bash
ssh chatbot@your-server-ip
```

---

## Server Network Configuration

### External API Access

The server cannot reach external municipality APIs via the public domain name due to firewall restrictions.
Use the **internal IP** instead:

| Public Domain | Internal IP | Notes |
|---------------|-------------|-------|
| `egate.hebron-city.ps` | `192.168.100.2` | Municipality e-gate server |

### Configuring API Integrations

When adding API integrations in the admin portal:
- **BaseUrl**: Use `http://192.168.100.2:8282` instead of `http://egate.hebron-city.ps:8282`
- **AllowlistedDomain**: Set to `192.168.100.2`

### Testing Server Network Access

```bash
# Test if server can reach the internal API
curl -v "http://192.168.100.2:8282/api/WaterAPIController/Water_s_plan"

# If this works but external domain doesn't, firewall is blocking outbound
curl -v "http://egate.hebron-city.ps:8282/api/WaterAPIController/Water_s_plan"
```

---

## Check Status

```bash
# List all running containers
docker ps

# Check specific container
docker ps | grep backend
docker ps | grep frontend
docker ps | grep qdrant
docker ps | grep postgres
```

---

## View Logs

### Backend Logs (Most Common)
```bash
# Last 100 lines
docker logs municipality-chatbot-backend-1 --tail 100

# Follow logs in real-time
docker logs municipality-chatbot-backend-1 -f

# With timestamps
docker logs municipality-chatbot-backend-1 --tail 100 -t

# Save to file
docker logs municipality-chatbot-backend-1 --tail 500 > backend_logs.txt
```

### Other Services
```bash
docker logs municipality-chatbot-frontend-1 --tail 50
docker logs municipality-chatbot-qdrant-1 --tail 50
docker logs municipality-chatbot-postgres-1 --tail 50
```

---

## Deploy Updates

### Quick Update (Most Common)
```bash
cd ~
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d --force-recreate
```

### Update Backend Only
```bash
docker pull superwae/municipality-chatbot-backend:latest
docker compose -f docker-compose.prod.yml up -d backend
```

### Update Frontend Only
```bash
docker pull superwae/municipality-chatbot-frontend:latest
docker compose -f docker-compose.prod.yml up -d frontend
```

### Restart Services
```bash
# Restart specific service
docker compose -f docker-compose.prod.yml restart backend

# Restart all
docker compose -f docker-compose.prod.yml restart

# Force recreate (clears container state)
docker compose -f docker-compose.prod.yml up -d --force-recreate backend
```

---

## Environment Variables

### View Current Config
```bash
# View .env file
cat ~/.env

# View container's env vars
docker inspect municipality-chatbot-backend-1 --format='{{.Config.Env}}'
```

### Edit Configuration
```bash
nano ~/.env
# After editing, restart to apply:
docker compose -f docker-compose.prod.yml up -d --force-recreate
```

### Required Variables
```bash
POSTGRES_PASSWORD=your_db_password
AUTH__JWT__SIGNING_KEY=YourVeryLongRandomSecretKeyAtLeast32Chars
SEED__ADMIN__PASSWORD=admin_password
OPENAI__API_KEY=sk-your-openai-key
Cors__AllowedOrigins=*
Cors__WidgetAllowedOrigins=*
```

---

## Database Operations

### Access PostgreSQL
```bash
docker exec -it municipality-chatbot-postgres-1 psql -U postgres -d municipality_chatbot
```

### Common SQL Commands
```sql
-- View table counts
SELECT 'faqs' as t, COUNT(*) FROM faqs
UNION SELECT 'documents', COUNT(*) FROM documents
UNION SELECT 'chat_sessions', COUNT(*) FROM chat_sessions;

-- Clear chat history (keep FAQs and docs)
DELETE FROM api_call_audits;
DELETE FROM routing_decisions;
DELETE FROM chat_messages;
DELETE FROM chat_sessions;

-- Full cleanup (keeps employees only)
DELETE FROM api_call_audits;
DELETE FROM routing_decisions;
DELETE FROM chat_messages;
DELETE FROM chat_sessions;
DELETE FROM faqs;
DELETE FROM document_chunks;
DELETE FROM documents;
DELETE FROM api_definitions;
```

### Access Qdrant
```bash
# Check collection status
curl http://localhost:6333/collections/municipality_knowledge

# Delete and recreate collection (full reset)
curl -X DELETE http://localhost:6333/collections/municipality_knowledge
```

---

## Full Database Cleanup

### Clean Everything (Keep Employee Accounts)

Use this to reset the system while preserving admin accounts.

```bash
# 1. Clean Qdrant vectors
curl -X DELETE http://localhost:6333/collections/municipality_knowledge

# 2. Clean PostgreSQL data
docker exec -it municipality-chatbot-postgres-1 psql -U postgres -d municipality_chatbot -c "
DELETE FROM api_call_audits;
DELETE FROM routing_decisions;
DELETE FROM chat_messages;
DELETE FROM chat_sessions;
DELETE FROM faqs;
DELETE FROM document_chunks;
DELETE FROM documents;
DELETE FROM api_definitions;
SELECT 'Cleanup complete. Employee accounts preserved.' as status;
"
```

### What Gets Deleted
- All FAQ questions and answers
- All uploaded documents and their chunks
- All chat history and messages
- All routing decisions
- All API integration definitions
- All vectors from Qdrant

### What Gets Kept
- Employee user accounts (admin, etc.)
- Employee passwords and roles
- Database schema and structure

---

## Troubleshooting

### Qdrant "Too Many Open Files" Error

**Symptoms:**
- Website crawler fails with 500 Internal Server Error
- Backend returns 502 Bad Gateway
- Qdrant logs show: `Too many open files (os error 24)`

**Quick Fix (restart all services):**
```bash
# Stop all containers
docker compose -f docker-compose.prod.yml down

# Start all containers
docker compose -f docker-compose.prod.yml up -d

# Wait for services to initialize
sleep 15

# Verify all containers are running
docker ps
```

**Permanent Fix (increase file limits):**

Add `ulimits` to the Qdrant service in `docker-compose.prod.yml`:

```yaml
qdrant:
  image: qdrant/qdrant:v1.11.5
  ulimits:
    nofile:
      soft: 65536
      hard: 65536
  # ... rest of qdrant config
```

Then recreate the container:
```bash
docker compose -f docker-compose.prod.yml up -d --force-recreate qdrant
```

**Why this happens:**
Qdrant creates many file handles for vector storage. The default Linux limit (1024) can be exhausted during heavy operations like website crawling. Increasing the limit to 65536 prevents this issue.

---

### Container Crashing (ExitCode 139)
```bash
# Check logs for error
docker logs municipality-chatbot-backend-1

# Verify environment variables
cat ~/.env

# Try manual restart
docker stop municipality-chatbot-backend-1
docker rm municipality-chatbot-backend-1
docker compose -f docker-compose.prod.yml up -d backend
```

### "Name or service not known" Error
```bash
# Check network
docker network ls
docker network inspect municipality-chatbot_default

# Ensure all containers on same network
docker compose -f docker-compose.prod.yml up -d
```

### API Calls Failing
```bash
# Test external API directly
curl -v "http://egate.hebron-city.ps:8282/api/WaterAPIController/Water_s_plan"

# Check backend logs for details
docker logs municipality-chatbot-backend-1 --tail 50
```

### Port Already in Use
```bash
# Find what's using port
sudo lsof -i :8080
sudo lsof -i :80

# Kill process if needed
sudo kill -9 <PID>
```

### Disk Space Issues
```bash
# Check disk usage
df -h

# Clean up Docker
docker system prune -a
docker volume prune
```

---

## Local Development

### Windows Commands
```powershell
cd f:/projects/CHATBOT

# Build and restart
docker compose build backend
docker compose up -d backend

# View logs
docker compose logs backend --tail 100

# Push to Docker Hub
docker tag municipality-chatbot-backend:latest superwae/municipality-chatbot-backend:latest
docker push superwae/municipality-chatbot-backend:latest

docker tag municipality-chatbot-frontend:latest superwae/municipality-chatbot-frontend:latest
docker push superwae/municipality-chatbot-frontend:latest
```

### Full Rebuild (After Major Changes)
```powershell
docker compose build --no-cache
docker compose up -d --force-recreate
```

---

## Important Files

### On Server
```
~/.env                      # Environment variables
~/docker-compose.prod.yml   # Production compose file
```

### On Local Machine
```
f:/projects/CHATBOT/
├── .env                    # Local environment
├── docker-compose.yml      # Local development
├── docker-compose.prod.yml # Production template
├── CLAUDE.md               # Project context for AI
└── docs/                   # Documentation
```

---

## Quick Reference

| Task | Command |
|------|---------|
| Check status | `docker ps` |
| View backend logs | `docker logs municipality-chatbot-backend-1 --tail 100` |
| Deploy update | `docker compose -f docker-compose.prod.yml pull && docker compose -f docker-compose.prod.yml up -d --force-recreate` |
| Restart backend | `docker compose -f docker-compose.prod.yml restart backend` |
| Edit env vars | `nano ~/.env` |
| Access database | `docker exec -it municipality-chatbot-postgres-1 psql -U postgres -d municipality_chatbot` |
