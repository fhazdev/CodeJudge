import {
  LogLevel,
  PublicClientApplication,
  type Configuration,
  type RedirectRequest,
} from '@azure/msal-browser'

/**
 * None of these values are secrets. A SPA is a public client precisely because it cannot
 * keep one: everything it ships is readable in devtools, which is why the flow uses PKCE
 * instead of a client secret. Defaults are baked in so `npm run dev` works with no setup,
 * and the VITE_ variables exist for pointing at a different registration.
 */
const clientId = import.meta.env.VITE_ENTRA_CLIENT_ID ?? 'b209f376-d96a-4b22-aa37-ad9333532998'

const apiClientId = import.meta.env.VITE_ENTRA_API_CLIENT_ID ?? '817d2b95-0ccf-4245-a431-6141e8370be7'

export const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5199'

/**
 * The scope the API validates. Requesting this is what makes Entra issue an access token
 * whose audience is the API, rather than only an ID token.
 */
export const apiScope = `api://${apiClientId}/access_as_user`

export const msalConfig: Configuration = {
  auth: {
    clientId,

    /**
     * "common", not a tenant id. /organizations silently excludes personal Microsoft
     * accounts, and a specific tenant excludes everyone outside it, which would defeat
     * the point of a live demo an interviewer can sign in to.
     */
    authority: import.meta.env.VITE_ENTRA_AUTHORITY ?? 'https://login.microsoftonline.com/common',

    /**
     * Must match a registered redirect URI byte for byte, including the trailing slash.
     * Entra rejects a root redirect URI registered without one, and window.location.origin
     * has no trailing slash, so spelling it out here is deliberate rather than redundant.
     */
    redirectUri: import.meta.env.VITE_ENTRA_REDIRECT_URI ?? `${window.location.origin}/`,
    postLogoutRedirectUri: `${window.location.origin}/`,
  },
  cache: {
    // Survives a tab close, so a demo does not force a fresh sign-in on every reload.
    cacheLocation: 'localStorage',
  },
  system: {
    loggerOptions: {
      logLevel: import.meta.env.DEV ? LogLevel.Warning : LogLevel.Error,
      loggerCallback: (level, message, containsPii) => {
        if (containsPii) return
        if (level === LogLevel.Error) console.error(message)
        else if (level === LogLevel.Warning) console.warn(message)
      },
    },
  },
}

/**
 * openid and profile identify the user to the SPA; the API scope is what actually gets
 * an access token for the API. Asking for the API scope up front means the user consents
 * once, at sign-in, rather than being interrupted on their first real request.
 */
export const loginRequest: RedirectRequest = {
  scopes: ['openid', 'profile', apiScope],
}

export const msalInstance = new PublicClientApplication(msalConfig)
