# ExpenseWorkflow

Expense reports routed through a configurable approval workflow. Each company defines its own rules
(who approves, in what order, depending on amount and other conditions); rules are versioned and can
change over time without affecting reports already in review. Out-of-office delegates approve in place
of absent approvers.

- `api/` ASP.NET Core Web API (.NET 10), EF Core, SQLite
- `web/` React + TypeScript (Vite)
- `tests/` xUnit tests for the workflow engine and admin services

## Run

```
cd api && dotnet run --launch-profile http     # http://localhost:5158, creates and seeds a SQLite db in Development
cd web && npm install && npm run dev           # http://localhost:5173
```

There is no authentication yet. The API reads the acting user from the `X-User-Id` header and the web
app has an "Acting as" menu. Do not expose this beyond local development.

## Test

```
dotnet test
cd web && npm test
```
