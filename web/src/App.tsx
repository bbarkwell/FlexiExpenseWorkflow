import { BrowserRouter, Navigate, NavLink, Route, Routes, useLocation } from 'react-router-dom'
import { api } from './api'
import { Page } from './components/Common'
import { Approvals, OutOfOffice } from './pages/ApprovalsAndLeave'
import { MyReports, ReportDetail, ReportEditor } from './pages/Reports'
import { WorkflowEditor } from './pages/WorkflowEditor'
import { Workflows } from './pages/Workflows'
import type { PendingApproval } from './types'
import { useLoad } from './useLoad'
import { UserProvider, useMe } from './UserContext'

function Shell() {
  const { me, users, isAdmin, switchUser } = useMe()
  const location = useLocation()
  // Refetched on every navigation so the badge clears after you decide something.
  const pending = useLoad(() => api.get<PendingApproval[]>('/approvals/pending'), [me.id, location.pathname])
  const count = pending.data?.length ?? 0

  return (
    <>
      <header className="topbar">
        <strong className="brand">ExpenseWorkflow</strong>
        <nav>
          <NavLink to="/reports">My reports</NavLink>
          <NavLink to="/approvals">
            Approvals {count > 0 && <span className="count">{count}</span>}
          </NavLink>
          <NavLink to="/out-of-office">Out of office</NavLink>
          {isAdmin && <NavLink to="/admin/workflows">Workflows</NavLink>}
        </nav>
        <label className="who" title="There is no sign-in yet: pick who you are acting as.">
          Acting as
          <select value={me.id} onChange={(e) => switchUser(Number(e.target.value))}>
            {users.map((u) => (
              <option key={u.id} value={u.id}>
                {u.name}
                {u.roles.includes('Admin') ? ' (admin)' : ''}
              </option>
            ))}
          </select>
        </label>
      </header>

      {/* Keyed by user so every page reloads its data when you switch who you are acting as. */}
      <main key={me.id}>
        <Routes>
          <Route path="/" element={<Navigate to="/reports" replace />} />
          <Route path="/reports" element={<MyReports />} />
          <Route path="/reports/new" element={<ReportEditor />} />
          <Route path="/reports/:id" element={<ReportDetail />} />
          <Route path="/reports/:id/edit" element={<ReportEditor />} />
          <Route path="/approvals" element={<Approvals />} />
          <Route path="/out-of-office" element={<OutOfOffice />} />
          <Route path="/admin/workflows" element={isAdmin ? <Workflows /> : <NotAllowed />} />
          <Route path="/admin/workflows/:id" element={isAdmin ? <WorkflowEditor /> : <NotAllowed />} />
          <Route path="*" element={<Page title="Not found">That page doesn't exist.</Page>} />
        </Routes>
      </main>
    </>
  )
}

function NotAllowed() {
  return (
    <Page title="Admins only">
      <p className="muted">Switch to an admin in the "Acting as" menu to manage workflows.</p>
    </Page>
  )
}

export default function App() {
  return (
    <BrowserRouter>
      <UserProvider>
        <Shell />
      </UserProvider>
    </BrowserRouter>
  )
}
