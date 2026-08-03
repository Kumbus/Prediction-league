import { createBrowserRouter } from "react-router-dom"
import { AppShell } from "./AppShell"
import { LandingPage } from "./LandingPage"
import { RequireAdmin } from "./RequireAdmin"
import { RequireAuth } from "./RequireAuth"
import { SignInPage } from "./SignInPage"
import { TournamentsListPage } from "./admin/tournaments/TournamentsListPage"
import { TournamentFormPage } from "./admin/tournaments/TournamentFormPage"
import { TournamentDetailPage } from "./admin/tournaments/TournamentDetailPage"
import { PlayersListPage } from "./admin/players/PlayersListPage"
import { PlayerFormPage } from "./admin/players/PlayerFormPage"
import { PlayerImportPage } from "./admin/players/PlayerImportPage"
import { MatchFormPage } from "./admin/matches/MatchFormPage"
import { MatchImportPage } from "./admin/matches/MatchImportPage"
import { LeaguesListPage } from "./leagues/LeaguesListPage"
import { LeagueFormPage } from "./leagues/LeagueFormPage"
import { LeagueDetailPage } from "./leagues/LeagueDetailPage"

export const router = createBrowserRouter([
  { path: "/", element: <LandingPage /> },
  { path: "/sign-in", element: <SignInPage /> },
  {
    element: <RequireAuth />,
    children: [
      { path: "/app", element: <AppShell /> },
      { path: "/app/leagues", element: <LeaguesListPage /> },
      { path: "/app/leagues/new", element: <LeagueFormPage /> },
      { path: "/app/leagues/:id", element: <LeagueDetailPage /> },
    ],
  },
  {
    element: <RequireAdmin />,
    children: [
      { path: "/admin/tournaments", element: <TournamentsListPage /> },
      { path: "/admin/tournaments/new", element: <TournamentFormPage /> },
      { path: "/admin/tournaments/:id", element: <TournamentDetailPage /> },
      { path: "/admin/tournaments/:id/edit", element: <TournamentFormPage /> },
      { path: "/admin/tournaments/:tournamentId/matches/new", element: <MatchFormPage /> },
      { path: "/admin/tournaments/:tournamentId/matches/import", element: <MatchImportPage /> },
      { path: "/admin/matches/:matchId/edit", element: <MatchFormPage /> },
      { path: "/admin/players", element: <PlayersListPage /> },
      { path: "/admin/players/new", element: <PlayerFormPage /> },
      { path: "/admin/players/:id/edit", element: <PlayerFormPage /> },
      { path: "/admin/players/import", element: <PlayerImportPage /> },
    ],
  },
])
