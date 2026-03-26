# inventory.ai

AI-powered inventory management system.

## Tech Stack

| Layer    | Technology              |
|----------|-------------------------|
| Frontend | Angular (PWA)           |
| Backend  | C# / ASP.NET Core       |
| Mobile   | .NET MAUI               |

## Repository Structure

```
inventory.ai/
├── .github/
│   └── workflows/          # CI/CD pipelines
├── docs/                   # Architecture & API docs
├── src/
│   ├── frontend/           # Angular PWA
│   │   └── src/
│   │       ├── app/
│   │       ├── assets/
│   │       └── environments/
│   ├── backend/            # ASP.NET Core API
│   │   ├── InventoryAI.Api/
│   │   ├── InventoryAI.Core/
│   │   └── InventoryAI.Infrastructure/
│   └── mobile/             # .NET MAUI
│       └── InventoryAI.Mobile/
└── tests/
    ├── frontend/
    │   ├── unit/
    │   └── e2e/
    ├── backend/
    │   ├── unit/
    │   │   ├── InventoryAI.Api.Unit.Tests/
    │   │   └── InventoryAI.Core.Unit.Tests/
    │   └── integration/
    │       └── InventoryAI.Integration.Tests/
    └── mobile/
        └── InventoryAI.Mobile.Tests/
```

## Getting Started

### Frontend
```bash
cd src/frontend
npm install
ng serve
```

### Backend
```bash
cd src/backend
dotnet restore
dotnet run --project InventoryAI.Api
```

### Mobile
```bash
cd src/mobile/InventoryAI.Mobile
dotnet restore
dotnet build
```
