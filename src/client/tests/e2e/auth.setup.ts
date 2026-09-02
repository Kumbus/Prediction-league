import { expect, request, test as setup } from "@playwright/test"
import { assertAdminAuthorized, me, registerOrSignIn, signIn } from "./fixtures/api"
import {
  ADMIN_DISPLAY_NAME,
  ADMIN_EMAIL,
  ADMIN_PASSWORD,
  API_ORIGIN,
  MEMBER_DISPLAY_NAME,
  MEMBER_EMAIL,
  MEMBER_PASSWORD,
  adminStatePath,
  memberStatePath,
} from "./fixtures/run"

// The API sets its cookie on host "localhost", which is port-agnostic — so state captured here
// against :7182 is sent by a browser on :5173 too. That is what lets specs skip the UI login.
async function newApiContext() {
  return request.newContext({ baseURL: API_ORIGIN, ignoreHTTPSErrors: true })
}

setup("admin session is stored and actually carries admin rights", async () => {
  const api = await newApiContext()
  try {
    await registerOrSignIn(api, ADMIN_EMAIL, ADMIN_PASSWORD, ADMIN_DISPLAY_NAME)

    // Fail HERE, not three steps later inside graph construction. AdminEmailAllowlist is an
    // exact-match set over Admin:Emails, and configuration merges arrays BY INDEX — a personal
    // admin at Admin:Emails:0 in user-secrets silently replaces the entry in
    // appsettings.Development.json and this account stops being an admin.
    const profile = await me(api)
    expect(
      profile.isGlobalAdmin,
      `${ADMIN_EMAIL} is not an admin. Add it to Admin:Emails in ` +
        "src/server/PredictionLeague.Api/appsettings.Development.json (index 0), and make sure " +
        "no user-secret occupies Admin:Emails:0. Restart the API after changing either.",
    ).toBe(true)

    // On the run that FIRST promotes this account, the cookie in hand was minted before the
    // promotion, so it carries no "prediction:admin" claim — authenticated, but 403 on every
    // admin write. Sign in once more so the stored state is minted from IsGlobalAdmin = true.
    await signIn(api, ADMIN_EMAIL, ADMIN_PASSWORD)
    await assertAdminAuthorized(api)

    await api.storageState({ path: adminStatePath })
  } finally {
    await api.dispose()
  }
})

setup("member session is stored", async () => {
  const api = await newApiContext()
  try {
    await registerOrSignIn(api, MEMBER_EMAIL, MEMBER_PASSWORD, MEMBER_DISPLAY_NAME)
    await api.storageState({ path: memberStatePath })
  } finally {
    await api.dispose()
  }
})
