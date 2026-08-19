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
