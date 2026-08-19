import { useMsal } from '@azure/msal-react'
import { loginRequest } from '../auth/msalConfig'

export function SignInPage() {
  const { instance } = useMsal()

  return (
    <div className="signin">
      <h1>Solve problems. Get judged.</h1>
      <p className="signin-lead">
        Write C# against real test cases and have it run in an isolated sandbox.
      </p>

      <button
        type="button"
        className="button button-primary"
        onClick={() => void instance.loginRedirect(loginRequest)}
      >
        Sign in with Microsoft
      </button>

      {/*
        This note is not decoration. access_as_user is user-consentable, so most people
        can approve it themselves, but plenty of companies disable user consent
        tenant-wide. Someone signing in with a work account in such a tenant hits "Need
        admin approval" and is stuck, and no change on our side can fix it. Saying so up
        front costs nothing and saves the demo.
      */}
      <p className="signin-note">
        Any Microsoft account works. If your work or school account is blocked by an
        administrator, use a personal account (outlook.com, hotmail.com, or any address
        registered as a Microsoft account).
      </p>
    </div>
  )
}
