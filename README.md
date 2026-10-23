# ExpenseWorkflow

Expense reports routed through a configurable approval workflow. Each company defines its own rules
(who approves, in what order, depending on amount and other conditions). Rules are versioned, so they can
change over time without affecting reports already in review. If an approver is out of office, their
delegate approves in their place.

| Folder   | What it is                                                         |
| -------- | ------------------------------------------------------------------ |
| `api/`   | ASP.NET Core Web API (.NET 10), EF Core, SQLite                    |
| `web/`   | React + TypeScript frontend (Vite)                                 |
| `tests/` | xUnit tests for the workflow engine, delegation and admin services |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`dotnet --version` should print 10.x)
- [Node.js](https://nodejs.org/) 20.19 or newer, 22.12 or newer recommended (`node --version`), which includes npm

No database server is needed: the API uses a SQLite file it creates itself.

## Run it

You need two terminals, one for the API and one for the web app.

**1. Start the API**

```
cd api
dotnet run --launch-profile http
```

It listens on http://localhost:5158. On first start, in the Development environment, it creates
`api/expenseworkflow.db`, applies the migrations and loads sample data (see below). This only happens in
the Development environment, which the launch profile sets (plain `dotnet run` uses it too).

**2. Start the web app**

```
cd web
npm install
npm run dev
```

Open http://localhost:5173.

## Sample data

The sample company has this org chart and a published workflow with your example rules:

| Person        | Role in the org                                                         |
| ------------- | ----------------------------------------------------------------------- |
| Eve Employee  | Employee; reports to Mark                                               |
| Mark Manager  | Manager; reports to Dana. **Out of office** for 14 days, Mia covers     |
| Mia Manager   | Manager                                                                 |
| Dana Head     | Head of Engineering                                                     |
| Frank Finance | Holds the Finance role                                                  |
| Alice Admin   | Admin: can edit workflows                                               |

| Report total      | Approval chain                          |
| ----------------- | --------------------------------------- |
| under $500        | manager                                 |
| $500 to $5,000    | manager, then department head          |
| over $5,000       | manager, department head, then Finance  |

Exactly $500.00 and exactly $5,000.00 both fall in the middle band.

## Try it

There is no sign-in yet. Use the **Acting as** menu in the top right to switch between people.

1. Act as **Eve**, choose **New report**, add one line of 6000 and **Save and submit**. The approval
   chain shows Mia (covering for Mark), then Dana, then anyone with the Finance role.
2. Act as **Mia**, open **Approvals**, review and approve. The report moves on to Dana.
3. Act as **Dana**, then **Frank**, and approve to finish the report.
4. Act as **Alice**, open **Workflows**, and use **New version** to edit a copy of the rules. Use
   **Check for problems** and **Try it** to test a change, then **Publish**. Reports already in review
   keep the version they were submitted under.

The out-of-office window for Mark is relative to the first run, so after two weeks it has expired. To get
fresh sample data, stop the API, delete `api/expenseworkflow.db`, and start it again. You can also set your
own windows on the **Out of office** page.

## Configuration

| Setting                | Where                                   | Default                           |
| ---------------------- | --------------------------------------- | --------------------------------- |
| API address            | `api/Properties/launchSettings.json`    | `http://localhost:5158`           |
| Database file          | `api/appsettings.json` (`ConnectionStrings:Default`) | `Data Source=expenseworkflow.db` |
| API address used by web | `web/.env` (`VITE_API_URL`)            | `http://localhost:5158`           |
| Web origin the API allows (CORS) | `api/Program.cs`              | `http://localhost:5173`           |

If you change the API port, update `VITE_API_URL` and restart `npm run dev`. If you run the web app on a
different port, add that origin to the CORS policy in `api/Program.cs`.

## Tests

```
dotnet test                 # from the repository root: engine, delegation and admin tests
cd web && npm test          # frontend unit tests (condition editor and money handling)
```

The API tests build a throwaway in-memory SQLite database from the real migrations, so they need nothing
running.

## Troubleshooting

- **The page says "Could not reach the API"**: the API isn't running, or `VITE_API_URL` points at the wrong
  address. Check http://localhost:5158/api/users in a browser.
- **Requests fail with a CORS error**: the web app is not on http://localhost:5173. Use that port, or add
  your origin in `api/Program.cs`.
- **The API starts but there are no users**: it was started outside the Development environment (for
  example `ASPNETCORE_ENVIRONMENT=Production`), so the database was not created. Unset it and restart.
- **Port already in use**: stop the other process, or change the port as described under Configuration.

## Development notes

- **No authentication yet.** The API reads the acting user from the `X-User-Id` request header, and the web
  app's "Acting as" menu sets it. Anyone can act as anyone, so do not expose this beyond local development.
  Real authentication belongs in `api/Services/CurrentUser.cs` and `api/Services/RequireAdminAttribute.cs`.
- Amounts are stored and sent as integer cents.
- A published workflow version is immutable. Changing rules means creating a new version.
- The API's OpenAPI document is served at http://localhost:5158/openapi/v1.json in Development.
