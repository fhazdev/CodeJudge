import { STATUS_LABELS, type Submission } from '../api/types'

const ACCEPTED = 'Accepted'

export function SubmissionResult({
  submission,
  isSubmitting,
  timedOut,
  error,
}: {
  submission: Submission | null
  isSubmitting: boolean
  timedOut: boolean
  error: string | null
}) {
  if (error) {
    return (
      <div className="result result-error">
        <strong>Could not submit</strong>
        <p>{error}</p>
      </div>
    )
  }

  if (!submission) {
    return isSubmitting ? <div className="result result-pending">Submitting…</div> : null
  }

  const pending = !submission.isTerminal
  const accepted = submission.status === ACCEPTED

  if (pending) {
    return (
      <div className="result result-pending">
        <div className="result-heading">
          <span className="spinner" aria-hidden="true" />
          <strong>{STATUS_LABELS[submission.status]}</strong>
        </div>

        {timedOut ? (
          // Not an error. The judge is still working; only the polling gave up.
          <p>
            Still running. The verdict will be saved when it finishes, so refresh in a
            moment to see it.
          </p>
        ) : (
          <p className="muted small">
            The judge scales to zero, so the first submission after an idle period takes
            longer to start.
          </p>
        )}
      </div>
    )
  }

  return (
    <div className={`result ${accepted ? 'result-accepted' : 'result-rejected'}`}>
      <div className="result-heading">
        <strong>{STATUS_LABELS[submission.status]}</strong>

        {submission.failedCaseOrdinal !== null && (
          <span className="muted small">test case {submission.failedCaseOrdinal}</span>
        )}
      </div>

      {(submission.runtimeMs !== null || submission.memoryKb !== null) && (
        <p className="result-metrics">
          {submission.runtimeMs !== null && <span>{submission.runtimeMs} ms</span>}
          {submission.memoryKb !== null && (
            <span>{Math.round(submission.memoryKb / 1024)} MB</span>
          )}
        </p>
      )}

      {submission.stderrExcerpt && <pre className="result-detail">{submission.stderrExcerpt}</pre>}
    </div>
  )
}
