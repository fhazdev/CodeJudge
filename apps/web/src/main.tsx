import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { MsalProvider } from '@azure/msal-react'
import { EventType, type AuthenticationResult } from '@azure/msal-browser'
import { msalInstance } from './auth/msalConfig'
import App from './App'
import './index.css'

/**
 * MSAL v3 and later require an explicit initialize() before any other call, and it must
 * finish before React renders. Rendering first produces a component tree that briefly
 * believes nobody is signed in, which shows up as a sign-in button flashing on every
 * page load for an already-authenticated user.
 */
async function bootstrap() {
  await msalInstance.initialize()

  // Completes a redirect that is landing back on the page right now. Without this the
  // authorization code in the URL is never exchanged for tokens.
  await msalInstance.handleRedirectPromise()

  const accounts = msalInstance.getAllAccounts()
  if (accounts.length > 0) {
    msalInstance.setActiveAccount(accounts[0])
  }

  msalInstance.addEventCallback((event) => {
    if (event.eventType === EventType.LOGIN_SUCCESS && event.payload) {
      msalInstance.setActiveAccount((event.payload as AuthenticationResult).account)
    }
  })

  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <MsalProvider instance={msalInstance}>
        <BrowserRouter>
          <App />
        </BrowserRouter>
      </MsalProvider>
    </StrictMode>,
  )
}

void bootstrap()
