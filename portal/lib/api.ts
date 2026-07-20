import type {
  EventDetail,
  EventSummary,
  EventsList,
  FighterProfile,
  FightResultRow,
  WeighInRow,
} from "@/lib/types";

// Server-only base URL of the .NET public API. ISR (1h) + on-demand tag revalidation
// keep the portal fresh; see app/api/revalidate/route.ts.
const API_BASE = process.env.SFC_API_BASE ?? "http://localhost:5000";
const REVALIDATE_SECONDS = 3600;

async function apiGet<T>(path: string, tags: string[]): Promise<T | null> {
  let res: Response;
  try {
    res = await fetch(`${API_BASE}${path}`, {
      next: { revalidate: REVALIDATE_SECONDS, tags },
    });
  } catch {
    // API unreachable (e.g. dev server down) — treat as no content rather than crash the build.
    return null;
  }
  if (res.status === 404 || res.status === 204) return null;
  if (!res.ok) throw new Error(`GET ${path} failed: ${res.status}`);
  return (await res.json()) as T;
}

export function getNextEvent() {
  return apiGet<EventSummary>("/api/public/events/next", ["events"]);
}

export function getEvents() {
  return apiGet<EventsList>("/api/public/events", ["events"]);
}

export function getEvent(slug: string) {
  return apiGet<EventDetail>(`/api/public/events/${slug}`, ["events", `event:${slug}`]);
}

export function getEventResults(slug: string) {
  return apiGet<FightResultRow[]>(`/api/public/events/${slug}/results`, [
    "events",
    `event:${slug}`,
  ]);
}

export function getEventWeighIns(slug: string) {
  return apiGet<WeighInRow[]>(`/api/public/events/${slug}/weigh-ins`, [
    "events",
    `event:${slug}`,
  ]);
}

export function getFighter(slug: string) {
  return apiGet<FighterProfile>(`/api/public/fighters/${slug}`, [
    "fighters",
    `fighter:${slug}`,
  ]);
}
