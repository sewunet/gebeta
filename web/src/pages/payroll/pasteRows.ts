import type { EmployeeDto } from "../../lib/types";

export type PastedRow = { employeeId: string; amount: number };

/**
 * Rows pasted from a spreadsheet: an employee number and an amount on each line, separated by a tab, comma, semicolon or spaces
 * ("EMP-00001	1,500.00"). Amounts may use thousands commas. Unknown numbers and unreadable lines come back to show, not guessed.
 */
export function parsePastedRows(text: string, employees: EmployeeDto[]): { rows: PastedRow[]; problems: string[] } {
  const byNumber = new Map(employees.map((e) => [e.number.toUpperCase(), e]));
  const rows: PastedRow[] = [];
  const problems: string[] = [];
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim();
    if (!line) continue;
    const match = /^(\S+)[\t;, ]+([\d,]+(?:\.\d{1,2})?)\s*$/.exec(line);
    if (!match) { problems.push(line); continue; }
    const [, number = "", figure = ""] = match;
    const employee = byNumber.get(number.toUpperCase());
    const amount = Number(figure.replace(/,/g, ""));
    if (!employee || !(amount > 0)) { problems.push(line); continue; }
    const existing = rows.find((r) => r.employeeId === employee.id);
    if (existing) existing.amount += amount; else rows.push({ employeeId: employee.id, amount });
  }

  return { rows, problems };
}
