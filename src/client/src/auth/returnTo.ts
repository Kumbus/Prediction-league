// Where to send a user once they are signed in. Two carriers reach the same answer: router state
// for the in-SPA email path, and a `returnTo` query param for the Google path, which is a full
// page navigation and cannot keep router state alive.

// A destination is only ever a same-origin relative path. One leading slash, and never "//" or
// "/\" — browsers read both as protocol-relative URLs to another host, which would turn the
// query param into an open redirect.
export function safeReturnTo(value: string | null | undefined): string | null {
  if (!value) return null

  // Strip C0 controls and DEL before testing the shape. Browsers drop tab/CR/LF *while parsing* a
  // URL, so a tab wedged after the leading slash reaches the network as "//evil.com" — a prefix
  // check against the raw string would wave it straight through.
  const candidate = Array.from(value)
    .filter((ch) => {
      const code = ch.charCodeAt(0)
      return code > 0x1f && code !== 0x7f
    })
    .join("")

  if (!candidate.startsWith("/")) return null
  if (candidate.startsWith("//") || candidate.startsWith("/\\")) return null
  return candidate
}

// RequireAuth stores the blocked location as a router Location *object*, so the path has to be
// rebuilt from its parts — interpolating the object itself yields "[object Object]".
export function pathFromLocationState(state: unknown): string | null {
  if (typeof state !== "object" || state === null) return null
  const { from } = state as { from?: unknown }
  if (typeof from !== "object" || from === null) return null

  const { pathname, search } = from as { pathname?: unknown; search?: unknown }
  if (typeof pathname !== "string") return null

  return safeReturnTo(pathname + (typeof search === "string" ? search : ""))
}
