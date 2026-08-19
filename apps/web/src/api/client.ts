import { InteractionRequiredAuthError } from '@azure/msal-browser'
import { apiBaseUrl, apiScope, msalInstance } from '../auth/msalConfig'
import type { PagedResult, ProblemDetail, ProblemSummary, Submission } from './types'

export class ApiError extends Error {
  // Declared and assigned rather than a constructor parameter property: the template
  // enables erasableSyntaxOnly, which rules out TypeScript syntax that emits runtime code.
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

/**
 * Gets an access token for the API.
 *
 * The distinction that trips everyone up: this returns `accessToken`, never `idToken`.
 * The ID token says who the user is and is for this app to render a name; the access
 * token says this client may call that API on the user's behalf and carries the API's
 * client id as its audience. Sending the ID token instead produces a 401 with an audience
 * mismatch that reads as baffling until you know the two are different things.
 */
async function getAccessToken(): Promise<string> {
  const account = msalInstance.getActiveAccount() ?? msalInstance.getAllAccounts()[0]

  if (!account) {
    throw new ApiError(401, 'Not signed in.')
  }

  try {
    const result = await msalInstance.acquireTokenSilent({
      scopes: [apiScope],
      account,
    })
    return result.accessToken
  } catch (error) {
    if (error instanceof InteractionRequiredAuthError) {
      // Refresh token expired, or consent was revoked. Only a full interactive round
      // trip can fix it, so hand off to the redirect and let this call be abandoned.
      await msalInstance.acquireTokenRedirect({ scopes: [apiScope], account })
    }
    throw error
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = await getAccessToken()

  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      ...init?.headers,
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
    },
  })

  if (!response.ok) {
    throw new ApiError(response.status, await describeFailure(response))
  }

  return (await response.json()) as T
}

/**
 * The API returns RFC 7807 ProblemDetails for validation failures, so prefer its message
 * over a bare status code.
 */
async function describeFailure(response: Response): Promise<string> {
  try {
    const body = await response.json()

    if (body.errors && typeof body.errors === 'object') {
      return Object.values(body.errors).flat().join(' ')
    }

    return body.title ?? body.detail ?? response.statusText
  } catch {
    return response.statusText || `Request failed with status ${response.status}`
  }
}

export const api = {
  listProblems: (page = 1, pageSize = 20) =>
    request<PagedResult<ProblemSummary>>(`/api/problems?page=${page}&pageSize=${pageSize}`),

  getProblem: (slug: string) => request<ProblemDetail>(`/api/problems/${slug}`),

  createSubmission: (problemSlug: string, code: string) =>
    request<Submission>('/api/submissions', {
      method: 'POST',
      body: JSON.stringify({ problemSlug, language: 'csharp', code }),
    }),

  getSubmission: (id: string) => request<Submission>(`/api/submissions/${id}`),
}
