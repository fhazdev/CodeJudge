/**
 * Mirrors the DTOs in CodeJudge.Application.Problems.
 *
 * Note what is absent and must stay absent: harness code and hidden test cases. The API
 * never sends them, so there is nothing here to hold them.
 */

export type Difficulty = 'Easy' | 'Medium' | 'Hard'

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasNextPage: boolean
}

export interface ProblemSummary {
  slug: string
  title: string
  difficulty: Difficulty
}

/** Only ever built from a test case marked visible. */
export interface ProblemExample {
  ordinal: number
  input: string
  expectedOutput: string
}

export interface ProblemDetail {
  slug: string
  title: string
  difficulty: Difficulty
  statementMd: string
  constraintsMd: string | null
  starterCode: string
  timeLimitMs: number
  memoryLimitKb: number
  examples: ProblemExample[]
}

export type SubmissionStatus =
  | 'Queued'
  | 'Running'
  | 'Accepted'
  | 'WrongAnswer'
  | 'TimeLimitExceeded'
  | 'RuntimeError'
  | 'CompileError'
  | 'MemoryLimitExceeded'
  | 'InternalError'

export interface Submission {
  id: string
  problemSlug: string
  language: string
  status: SubmissionStatus
  /**
   * Computed by the API rather than derived here. Re-deriving "is this finished" from the
   * status union is exactly how a client and server drift apart when a status is added.
   */
  isTerminal: boolean
  runtimeMs: number | null
  memoryKb: number | null
  failedCaseOrdinal: number | null
  stderrExcerpt: string | null
  createdAt: string
  completedAt: string | null
}

export const STATUS_LABELS: Record<SubmissionStatus, string> = {
  Queued: 'Queued',
  Running: 'Running',
  Accepted: 'Accepted',
  WrongAnswer: 'Wrong Answer',
  TimeLimitExceeded: 'Time Limit Exceeded',
  RuntimeError: 'Runtime Error',
  CompileError: 'Compile Error',
  MemoryLimitExceeded: 'Memory Limit Exceeded',
  InternalError: 'Judge Error',
}
