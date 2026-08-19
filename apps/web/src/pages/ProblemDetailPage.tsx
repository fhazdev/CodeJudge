import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import Editor from '@monaco-editor/react'
import { api } from '../api/client'
import type { ProblemDetail } from '../api/types'
import { useSubmission } from '../hooks/useSubmission'
import { SubmissionResult } from '../components/SubmissionResult'

export function ProblemDetailPage() {
  const { slug } = useParams<{ slug: string }>()

  const [problem, setProblem] = useState<ProblemDetail | null>(null)
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)

  const { submission, isSubmitting, timedOut, error: submitError, submit, reset } =
    useSubmission(slug)

  // Keyed on the slug rather than reset inside the effect. Calling setState synchronously
  // in an effect triggers a second render pass before anything is painted; tracking the
  // slug the current state belongs to derives the same reset during render instead.
  const [loadedSlug, setLoadedSlug] = useState<string | undefined>(undefined)

  if (slug !== loadedSlug) {
    setLoadedSlug(slug)
    setProblem(null)
    setError(null)
    reset()
  }

  useEffect(() => {
    if (!slug) return

    let cancelled = false

    api
      .getProblem(slug)
      .then((result) => {
        if (cancelled) return
        setProblem(result)
        setCode(result.starterCode)
      })
      .catch((err: Error) => {
        if (!cancelled) setError(err.message)
      })

    return () => {
      cancelled = true
    }
  }, [slug])

  if (error) {
    return (
      <>
        <Link to="/" className="back-link">
          ← All problems
        </Link>
        <p className="error">Could not load this problem: {error}</p>
      </>
    )
  }

  if (!problem) {
    return <p className="muted">Loading…</p>
  }

  return (
    <>
      <Link to="/" className="back-link">
        ← All problems
      </Link>

      <div className="problem-layout">
        <section className="problem-statement">
          <div className="problem-heading">
            <h1>{problem.title}</h1>
            <span className={`badge badge-${problem.difficulty.toLowerCase()}`}>
              {problem.difficulty}
            </span>
          </div>

          {/*
            Rendered as preformatted text rather than through a markdown library.
            Statements are authored by us, not by users, so this is a deliberate
            "not yet worth a dependency" rather than an oversight. Phase 3 swaps it.
          */}
          <pre className="prose">{problem.statementMd}</pre>

          {problem.constraintsMd && (
            <>
              <h2>Constraints</h2>
              <pre className="prose">{problem.constraintsMd}</pre>
            </>
          )}

          {problem.examples.length > 0 && (
            <>
              <h2>Examples</h2>
              {problem.examples.map((example) => (
                <div key={example.ordinal} className="example">
                  <div>
                    <span className="example-label">Input</span>
                    <pre>{example.input}</pre>
                  </div>
                  <div>
                    <span className="example-label">Output</span>
                    <pre>{example.expectedOutput}</pre>
                  </div>
                </div>
              ))}
            </>
          )}

          <p className="limits">
            Time limit {problem.timeLimitMs} ms · Memory limit{' '}
            {Math.round(problem.memoryLimitKb / 1024)} MB
          </p>
        </section>

        <section className="problem-editor">
          <div className="editor-shell">
            <Editor
              height="100%"
              language="csharp"
              theme="vs-dark"
              value={code}
              onChange={(value) => setCode(value ?? '')}
              options={{
                minimap: { enabled: false },
                fontSize: 14,
                scrollBeyondLastLine: false,
                automaticLayout: true,
                tabSize: 4,
              }}
            />
          </div>

          <div className="editor-actions">
            <button
              type="button"
              className="button button-ghost"
              disabled={isSubmitting}
              onClick={() => {
                setCode(problem.starterCode)
                reset()
              }}
            >
              Reset
            </button>

            <button
              type="button"
              className="button button-primary"
              disabled={isSubmitting || code.trim().length === 0}
              onClick={() => void submit(code)}
            >
              {isSubmitting ? 'Judging…' : 'Submit'}
            </button>
          </div>

          <SubmissionResult
            submission={submission}
            isSubmitting={isSubmitting}
            timedOut={timedOut}
            error={submitError}
          />

          <p className="muted small">
            Write a <code>Solution</code> class. There is no <code>Main</code>: each
            problem supplies a harness that calls your method.
          </p>
        </section>
      </div>
    </>
  )
}
