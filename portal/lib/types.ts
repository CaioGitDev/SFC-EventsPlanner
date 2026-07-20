// Mirror of the .NET public API DTOs (src/Sfc.Web/Services/PublicContentService.cs).
// Enum-name fields arrive as raw English codes — the portal maps them to pt-PT labels.

export type EventStatus = "Draft" | "Published" | "Completed" | "Cancelled";
export type FightStatus = "Scheduled" | "Completed" | "Cancelled" | "NoContest";
export type Billing = "Main" | "CoMain" | "Card";
export type Corner = "red" | "blue";
export type ResultMethod =
  | "Ko"
  | "Tko"
  | "UnanimousDecision"
  | "SplitDecision"
  | "MajorityDecision"
  | "Draw"
  | "NoContest"
  | "Disqualification"
  | "Forfeit";

export interface EventSummary {
  name: string;
  slug: string;
  date: string;
  venue: string | null;
  city: string | null;
  status: EventStatus;
  bannerUrl: string | null;
  posterUrl: string | null;
  ticketsUrl: string | null;
  streamUrl: string | null;
  fightCount: number;
}

export interface EventsList {
  upcoming: EventSummary[];
  past: EventSummary[];
}

/** Name is always present; the rest is null for athletes without public-profile consent. */
export interface CardAthlete {
  name: string;
  nickname: string | null;
  slug: string | null;
  photoUrl: string | null;
  nationality: string | null;
  age: number | null;
  record: string | null;
  clubName: string | null;
}

export interface FightCardEntry {
  order: number;
  billing: Billing;
  discipline: string;
  rounds: number;
  roundDurationMinutes: number;
  weightClass: string | null;
  catchweightKg: number | null;
  isTitleFight: boolean;
  isAmateur: boolean;
  status: FightStatus;
  red: CardAthlete;
  blue: CardAthlete;
}

export interface EventDetail {
  name: string;
  slug: string;
  date: string;
  venue: string | null;
  city: string | null;
  status: EventStatus;
  description: string | null;
  bannerUrl: string | null;
  posterUrl: string | null;
  ticketsUrl: string | null;
  streamUrl: string | null;
  fights: FightCardEntry[];
}

export interface ResultInfo {
  winnerCorner: Corner | null;
  method: ResultMethod;
  round: number | null;
  time: string | null;
}

export interface FightResultRow {
  order: number;
  billing: Billing;
  discipline: string;
  weightClass: string | null;
  catchweightKg: number | null;
  isTitleFight: boolean;
  isAmateur: boolean;
  red: CardAthlete;
  blue: CardAthlete;
  status: FightStatus;
  result: ResultInfo | null;
}

export interface WeighInRow {
  order: number;
  athleteName: string;
  athleteSlug: string | null;
  corner: Corner;
  weightClass: string | null;
  catchweightKg: number | null;
  officialWeightKg: number | null;
  weighedAt: string | null;
  missedWeight: boolean;
}

export interface FighterFightRow {
  eventName: string;
  eventSlug: string;
  eventDate: string;
  opponentName: string;
  opponentSlug: string | null;
  summary: string;
}

export interface UpcomingFight {
  eventName: string;
  eventSlug: string;
  eventDate: string;
  opponentName: string;
  opponentSlug: string | null;
}

export interface FighterProfile {
  name: string;
  nickname: string | null;
  slug: string;
  photoUrl: string | null;
  nationality: string;
  age: number;
  discipline: string;
  status: string;
  clubName: string | null;
  record: string;
  winsByKo: number;
  lastFights: FighterFightRow[];
  nextFight: UpcomingFight | null;
}
