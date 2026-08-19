import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { ProblemSummary } from '../api/types'

export function ProblemListPage() {
  const [problems, setProblems] = useState<ProblemSummary[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false

    api
      .listProblems()
      .then((result) => {
        if (!cancelled) setProblems(result.items)
      })
      .catch((err: Error) => {
        if (!cancelled) setError(err.message)
      })

    return () => {
      cancelled = true
    }
  }, [])

  if (error) {
    return <p className="error">Could not load problems: {error}</p>
  }

  if (!problems) {
    return <p className="muted">Loading problems…</p>
  }

  return (
    <>
      <h1>Problems</h1>

      <ul className="problem-list">
        {problems.map((problem) => (
          <li key={problem.slug}>
            <Link to={`/problems/${problem.slug}`} className="problem-row">
              <span className="problem-title">{problem.title}</span>
              <span className={`badge badge-${problem.difficulty.toLowerCase()}`}>
                {problem.difficulty}
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </>
  )
}
