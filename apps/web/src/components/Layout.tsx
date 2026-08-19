import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useMsal } from '@azure/msal-react'

export function Layout({ children }: { children: ReactNode }) {
  const { instance, accounts } = useMsal()
  const account = accounts[0]

  return (
    <div className="app">
      <header className="app-header">
        <Link to="/" className="brand">
          Code<span>Judge</span>
        </Link>

        {account && (
          <div className="account">
            <span className="account-name">{account.name ?? account.username}</span>
            <button
              type="button"
              className="button button-ghost"
              onClick={() => void instance.logoutRedirect()}
            >
              Sign out
            </button>
          </div>
        )}
      </header>

      <main className="app-main">{children}</main>
    </div>
  )
}
