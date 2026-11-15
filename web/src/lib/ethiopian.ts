// The Ethiopian calendar, the same algorithm as Gebeta.SharedKernel.Ethiopia.EthiopianDate (C#).
// Dates are stored Gregorian; this is for display.

const JDN_EPOCH_OFFSET = 1_723_856;

export type EthiopianDate = { year: number; month: number; day: number };

export const MONTHS_EN = [
  "Meskerem", "Tikimt", "Hidar", "Tahsas", "Tir", "Yekatit", "Megabit",
  "Miyazya", "Ginbot", "Sene", "Hamle", "Nehase", "Pagume",
];

export const MONTHS_AM = [
  "መስከረም", "ጥቅምት", "ኅዳር", "ታኅሣሥ", "ጥር", "የካቲት", "መጋቢት",
  "ሚያዝያ", "ግንቦት", "ሰኔ", "ሐምሌ", "ነሐሴ", "ጳጉሜን",
];

const mod = (a: number, b: number) => ((a % b) + b) % b;

/** Julian Day Number of a Gregorian date (year, month 1-12, day). */
function gregorianToJdn(y: number, m: number, d: number): number {
  const a = Math.floor((14 - m) / 12);
  const yy = y + 4800 - a;
  const mm = m + 12 * a - 3;
  return d + Math.floor((153 * mm + 2) / 5) + 365 * yy + Math.floor(yy / 4) - Math.floor(yy / 100) + Math.floor(yy / 400) - 32045;
}

/** Converts an ISO date (yyyy-mm-dd) to the Ethiopian calendar. */
export function toEthiopian(iso: string): EthiopianDate {
  const [y, m, d] = iso.slice(0, 10).split("-").map(Number) as [number, number, number];
  const jdn = gregorianToJdn(y, m, d);
  const r = mod(jdn - JDN_EPOCH_OFFSET, 1461);
  const n = mod(r, 365) + 365 * Math.floor(r / 1460);
  const year = 4 * Math.floor((jdn - JDN_EPOCH_OFFSET) / 1461) + Math.floor(r / 365) - Math.floor(r / 1460);
  return { year, month: Math.floor(n / 30) + 1, day: mod(n, 30) + 1 };
}

export function formatEthiopian(iso: string, amharic = false): string {
  const e = toEthiopian(iso);
  return amharic ? `${MONTHS_AM[e.month - 1]} ${e.day} ቀን ${e.year}` : `${MONTHS_EN[e.month - 1]} ${e.day}, ${e.year}`;
}

export type CalendarPreference = "Ethiopian" | "Gregorian" | "Both";

/** A date as the user prefers to see it. */
export function formatDate(iso: string, calendar: CalendarPreference, amharic: boolean): string {
  const gregorian = new Date(`${iso.slice(0, 10)}T00:00:00`).toLocaleDateString(amharic ? "am-ET" : "en-GB", {
    day: "numeric",
    month: "short",
    year: "numeric",
  });
  if (calendar === "Gregorian") return gregorian;
  const ethiopian = formatEthiopian(iso, amharic);
  return calendar === "Ethiopian" ? ethiopian : `${ethiopian} (${gregorian})`;
}

/** Ethiopian birr with santim, e.g. "ETB 13,000.00". */
export function formatMoney(amount: number, currency = "ETB"): string {
  return `${currency} ${amount.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

export const todayIso = () => {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
};
