// Event dates are naive local (Lisbon wall-clock) ISO strings like "2026-07-10T20:00:00".
// We format the components directly — never via Date timezone math — so the displayed
// time matches the weigh-in-room clock regardless of the server/runtime timezone.

const MONTHS_PT = [
  "janeiro", "fevereiro", "março", "abril", "maio", "junho",
  "julho", "agosto", "setembro", "outubro", "novembro", "dezembro",
];

interface DateParts {
  year: number;
  month: number; // 1-12
  day: number;
  hour: number;
  minute: number;
}

function parseNaive(iso: string): DateParts | null {
  const match = iso.match(/^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2}))?/);
  if (!match) return null;
  return {
    year: Number(match[1]),
    month: Number(match[2]),
    day: Number(match[3]),
    hour: match[4] ? Number(match[4]) : 0,
    minute: match[5] ? Number(match[5]) : 0,
  };
}

/** e.g. "10 de julho de 2026". */
export function formatDate(iso: string): string {
  const p = parseNaive(iso);
  if (!p) return iso;
  return `${p.day} de ${MONTHS_PT[p.month - 1]} de ${p.year}`;
}

/** e.g. "10 de julho de 2026, 20:00". */
export function formatDateTime(iso: string): string {
  const p = parseNaive(iso);
  if (!p) return iso;
  const time = `${String(p.hour).padStart(2, "0")}:${String(p.minute).padStart(2, "0")}`;
  return `${formatDate(iso)}, ${time}`;
}

/** e.g. "20:00". */
export function formatTime(iso: string): string {
  const p = parseNaive(iso);
  if (!p) return "";
  return `${String(p.hour).padStart(2, "0")}:${String(p.minute).padStart(2, "0")}`;
}

function lisbonTodayParts(): { year: number; month: number; day: number } {
  const fmt = new Intl.DateTimeFormat("en-CA", {
    timeZone: "Europe/Lisbon",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  });
  const [year, month, day] = fmt.format(new Date()).split("-").map(Number);
  return { year, month, day };
}

/** True when the event's calendar date is today in Lisbon — gates the "Ver em direto" CTA. */
export function isTodayInLisbon(iso: string): boolean {
  const p = parseNaive(iso);
  if (!p) return false;
  const t = lisbonTodayParts();
  return p.year === t.year && p.month === t.month && p.day === t.day;
}

/**
 * UTC epoch (ms) of a Lisbon wall-clock naive ISO, DST-aware — so the countdown
 * stays aligned with the Lisbon-based "Ver em direto" gate for visitors in any
 * timezone. Falls back to naive Date parsing if the string is unparseable.
 */
export function lisbonEpochMs(iso: string): number {
  const p = parseNaive(iso);
  if (!p) return new Date(iso).getTime();

  // Treat the wall time as UTC, then measure how far Lisbon is from UTC at that instant.
  const guess = Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute);
  const seen = new Intl.DateTimeFormat("en-GB", {
    timeZone: "Europe/Lisbon",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  }).formatToParts(new Date(guess));
  const field = (type: string) => Number(seen.find((x) => x.type === type)?.value);
  const seenUtc = Date.UTC(
    field("year"),
    field("month") - 1,
    field("day"),
    field("hour") % 24,
    field("minute"),
  );
  const offset = seenUtc - guess; // ms Lisbon is ahead of UTC at this date
  return guess - offset;
}

/** Weight in kg with the pt-PT decimal comma, e.g. 65.3 → "65,3". */
export function formatKg(kg: number): string {
  return String(kg).replace(".", ",");
}
