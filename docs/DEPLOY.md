# Municipality Chatbot - Deployment Guide

## Overview

This application runs on Docker using pre-built images from Docker Hub:
- **Backend**: `superwae/municipality-chatbot-backend:latest`
- **Frontend**: `superwae/municipality-chatbot-frontend:latest`

## Server Requirements

- Ubuntu Linux (tested on 22.04)
- Docker & Docker Compose installed
- Ports 80, 8080, 5432, 6333 available

## Initial Setup

### 1. Create project directory

```bash
mkdir ~/chatbot && cd ~/chatbot
```

### 2. Create `.env` file

```bash
cat > .env << 'EOF'
POSTGRES_PASSWORD=2022
AUTH__JWT__SIGNING_KEY=YourVeryLongRandomSecretKeyAtLeast32Chars
SEED__ADMIN__PASSWORD=admin123
OPENAI__API_KEY=your-openai-api-key-here
Cors__AllowedOrigins=*
Cors__WidgetAllowedOrigins=*
EOF
```

### 3. Create `docker-compose.prod.yml`

```bash
cat > docker-compose.prod.yml << 'EOF'
name: municipality-chatbot

services:
  qdrant:
    image: qdrant/qdrant:v1.11.5
    restart: unless-stopped
    ports:
      - "6333:6333"
      - "6334:6334"
    volumes:
      - qdrant_data:/qdrant/storage

  postgres:
    image: postgres:16-alpine
    restart: unless-stopped
    environment:
      POSTGRES_DB: municipality_chatbot
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    ports:
      - "5432:5432"
    volumes:
      - pg_data:/var/lib/postgresql/data

  backend:
    image: superwae/municipality-chatbot-backend:latest
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://0.0.0.0:8080
      DATABASE__AUTOMIGRATE: "true"
      SEED__ADMIN__ENABLED: "true"
      SEED__ADMIN__USERNAME: ${SEED__ADMIN__USERNAME:-admin}
      SEED__ADMIN__PASSWORD: ${SEED__ADMIN__PASSWORD}
      SEED__ADMIN__ROLE: ${SEED__ADMIN__ROLE:-EmployeeAdmin}
      LLM__PROVIDER: ${LLM__PROVIDER:-OpenAI}
      OPENAI__API_KEY: ${OPENAI__API_KEY}
      OPENAI__BASE_URL: ${OPENAI__BASE_URL:-https://api.openai.com/v1}
      OPENAI__MODEL: ${OPENAI__MODEL:-gpt-4o-mini}
      OPENAI__EMBEDDING_MODEL: ${OPENAI__EMBEDDING_MODEL:-text-embedding-3-large}
      QDRANT__URL: http://qdrant:6333
      QDRANT__COLLECTION: ${QDRANT__COLLECTION:-municipality_knowledge}
      QDRANT__VECTOR_SIZE: ${QDRANT__VECTOR_SIZE:-3072}
      POSTGRES__CONNECTION_STRING: Host=postgres;Database=municipality_chatbot;Username=postgres;Password=${POSTGRES_PASSWORD}
      AUTH__JWT__ISSUER: ${AUTH__JWT__ISSUER:-municipality-chatbot}
      AUTH__JWT__AUDIENCE: ${AUTH__JWT__AUDIENCE:-municipality-chatbot}
      AUTH__JWT__SIGNING_KEY: ${AUTH__JWT__SIGNING_KEY}
      AUTH__JWT__ACCESS_TOKEN_MINUTES: ${AUTH__JWT__ACCESS_TOKEN_MINUTES:-60}
      Cors__AllowedOrigins: "*"
      Cors__WidgetAllowedOrigins: "*"
    ports:
      - "8080:8080"
    depends_on:
      - qdrant
      - postgres

  frontend:
    image: superwae/municipality-chatbot-frontend:latest
    restart: unless-stopped
    ports:
      - "80:80"
    depends_on:
      - backend

volumes:
  qdrant_data:
  pg_data:
EOF
```

### 4. Start the application

```bash
docker compose -f docker-compose.prod.yml up -d
```

### 5. Access the application

- **Frontend**: `http://YOUR_SERVER_IP`
- **Backend API**: `http://YOUR_SERVER_IP:8080`
- **Admin login**: username `admin`, password from `.env`

## Useful Commands

```bash
# View logs
docker compose -f docker-compose.prod.yml logs -f

# View specific service logs
docker compose -f docker-compose.prod.yml logs -f backend

# Stop all services
docker compose -f docker-compose.prod.yml down

# Restart a specific service
docker compose -f docker-compose.prod.yml restart backend
```

## Architecture

```
Browser (port 80)
    │
    ▼
┌─────────────┐
│   Nginx     │  (Frontend container)
│  (port 80)  │
└─────────────┘
    │
    │ /api/* requests proxied
    ▼
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   Backend   │────▶│  PostgreSQL │     │   Qdrant    │
│ (port 8080) │     │ (port 5432) │     │ (port 6333) │
└─────────────┘     └─────────────┘     └─────────────┘
    │
    │ OpenAI API calls
    ▼
┌─────────────┐
│   OpenAI    │
│     API     │
└─────────────┘
```

---

## Quick Update

When code changes are made, run these commands:

**On your development machine (Windows):**

```powershell
# Build and push backend
docker build --no-cache -t superwae/municipality-chatbot-backend:latest -f backend/Dockerfile .
docker push superwae/municipality-chatbot-backend:latest

# Build and push frontend
docker build --no-cache -t superwae/municipality-chatbot-frontend:latest -f frontend/Dockerfile .
docker push superwae/municipality-chatbot-frontend:latest
```

**On the server:**

```bash
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d --force-recreate
```
