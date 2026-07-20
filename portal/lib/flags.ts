// Best-effort nationality → flag emoji. Nationality is free text in the backoffice,
// so this maps the common pt-PT/EN names and ISO-3166 alpha-2 codes; anything unknown
// renders without a flag. (A normalized ISO field in the backoffice is a future call.)

const NAME_TO_ISO: Record<string, string> = {
  portugal: "PT",
  portuguesa: "PT",
  portugues: "PT",
  brasil: "BR",
  brazil: "BR",
  brasileiro: "BR",
  brasileira: "BR",
  espanha: "ES",
  spain: "ES",
  espanhol: "ES",
  franca: "FR",
  france: "FR",
  frances: "FR",
  "reino unido": "GB",
  "united kingdom": "GB",
  inglaterra: "GB",
  england: "GB",
  irlanda: "IE",
  ireland: "IE",
  holanda: "NL",
  "paises baixos": "NL",
  netherlands: "NL",
  italia: "IT",
  italy: "IT",
  alemanha: "DE",
  germany: "DE",
  belgica: "BE",
  belgium: "BE",
  marrocos: "MA",
  morocco: "MA",
  angola: "AO",
  mocambique: "MZ",
  "cabo verde": "CV",
  "estados unidos": "US",
  "united states": "US",
  usa: "US",
  tailandia: "TH",
  thailand: "TH",
  russia: "RU",
  ucrania: "UA",
  ukraine: "UA",
  polonia: "PL",
  poland: "PL",
};

const COMBINING_MARKS = new RegExp("[\\u0300-\\u036f]", "g");

function stripDiacritics(value: string): string {
  return value.normalize("NFD").replace(COMBINING_MARKS, "");
}

function isoToEmoji(iso: string): string {
  return iso
    .toUpperCase()
    .replace(/[^A-Z]/g, "")
    .replace(/./g, (c) => String.fromCodePoint(0x1f1e6 + c.charCodeAt(0) - 65));
}

/** Flag emoji for a free-text nationality, or null when it can't be resolved. */
export function flagFor(nationality: string | null | undefined): string | null {
  if (!nationality) return null;
  const key = stripDiacritics(nationality.trim().toLowerCase());
  if (/^[a-z]{2}$/.test(key)) return isoToEmoji(key);
  const iso = NAME_TO_ISO[key];
  return iso ? isoToEmoji(iso) : null;
}
