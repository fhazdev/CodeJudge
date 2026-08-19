import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import type { Submission } from '../api/types'

/**
 * Polling policy. Section 7 of the build plan: 1s, easing to 3s after 10s, giving up at
 * 120s.
 *
 * Giving up is a display concession, not a cancellation. The judge keeps working and the
 * verdict still lands in the database, so the message is "still running, refresh to
 * check" and never an error.
 */
const FAST_INTERVAL_MS = 1000
const SLOW_INTERVAL_MS = 3000
const SLOW_AFTER_MS = 10_000
const GIVE_UP_AFTER_MS = 120_000

export interface SubmissionState {
  submission: Submission | null
  isSubmitting: boolean
  /** True once polling stopped while the verdict was still pending. */
  timedOut: boolean
  error: string | null
  submit: (code: string) => Promise<void>
  reset: () => void
}

export function useSubmission(problemSlug: string | undefined): SubmissionState {
  const [submission, setSubmission] = useState<Submission | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [timedOut, setTimedOut] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Held in a ref so an in-flight poll loop can be abandoned when the component unmounts
  // or a second submission starts, without it being a render dependency.
  const pollToken = useRef(0)

  const reset = useCallback(() => {
    pollToken.current += 1
    setSubmission(null)
    setTimedOut(false)
    setError(null)
  }, [])

  // Abandon any poll loop on unmount, so navigating away mid-judge does not leave a
  // timer running and calling setState on a dead component.
  useEffect(
    () => () => {
      pollToken.current += 1
    },
    [],
  )

  const submit = useCallback(
    async (code: string) => {
      if (!problemSlug) return

      const token = ++pollToken.current
      setIsSubmitting(true)
      setTimedOut(false)
      setError(null)
      setSubmission(null)

      try {
        const created = await api.createSubmission(problemSlug, code)
        if (pollToken.current !== token) return

        setSubmission(created)

        if (created.isTerminal) return

        const startedAt = Date.now()

        // A plain loop rather than setInterval: each poll waits for the previous response,
        // so a slow API cannot pile requests on top of each other.
        for (;;) {
          const elapsed = Date.now() - startedAt

          if (elapsed > GIVE_UP_AFTER_MS) {
            if (pollToken.current === token) setTimedOut(true)
            return
          }

          const delay = elapsed < SLOW_AFTER_MS ? FAST_INTERVAL_MS : SLOW_INTERVAL_MS
          await new Promise((resolve) => setTimeout(resolve, delay))

          if (pollToken.current !== token) return

          const latest = await api.getSubmission(created.id)
          if (pollToken.current !== token) return

          setSubmission(latest)
          if (latest.isTerminal) return
        }
      } catch (err) {
        if (pollToken.current === token) {
          setError(err instanceof Error ? err.message : 'Submission failed.')
        }
      } finally {
        if (pollToken.current === token) setIsSubmitting(false)
      }
    },
    [problemSlug],
  )

  return { submission, isSubmitting, timedOut, error, submit, reset }
}
