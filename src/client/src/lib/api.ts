export interface ProblemDetails {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  status: number
  problem?: ProblemDetails

  constructor(status: number, message: string, problem?: ProblemDetails) {
    super(message)
    this.name = "ApiError"
    this.status = status
    this.problem = problem
  }
}

const BASE_URL = import.meta.env.VITE_API_BASE_URL

export interface ApiFetchInit extends Omit<RequestInit, "body"> {
  body?: RequestInit["body"] | Record<string, unknown> | unknown[]
}

export async function apiFetch<T>(path: string, init: ApiFetchInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  let body: BodyInit | null | undefined = undefined

  if (init.body !== undefined && init.body !== null) {
    if (
      typeof init.body === "string" ||
      init.body instanceof FormData ||
      init.body instanceof Blob ||
      init.body instanceof ArrayBuffer ||
      init.body instanceof URLSearchParams ||
      init.body instanceof ReadableStream
    ) {
      body = init.body as BodyInit
    } else {
      body = JSON.stringify(init.body)
      if (!headers.has("Content-Type")) {
        headers.set("Content-Type", "application/json")
      }
    }
  }

  const response = await fetch(`${BASE_URL}${path}`, {
    ...init,
    body,
    headers,
    credentials: "include",
  })

  if (response.status === 204) {
    return undefined as T
  }

  if (!response.ok) {
    let problem: ProblemDetails | undefined
    try {
      problem = (await response.json()) as ProblemDetails
    } catch {
      problem = undefined
    }
    const message = problem?.title ?? problem?.detail ?? response.statusText ?? "Request failed"
    throw new ApiError(response.status, message, problem)
  }

  const text = await response.text()
  if (!text) return undefined as T
  return JSON.parse(text) as T
}
