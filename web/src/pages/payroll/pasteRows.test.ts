import { describe, expect, it } from "vitest";
import type { EmployeeDto } from "../../lib/types";
import { parsePastedRows } from "./pasteRows";

const employee = (id: string, number: string): EmployeeDto =>
  ({ id, number, companyId: "c", firstName: "", fatherName: "", fullName: number, gender: "Female", dateOfJoining: "2024-01-01", status: "Active" });

describe("pasted rows", () => {
  const staff = [employee("1", "EMP-00001"), employee("2", "EMP-00002")];

  it("reads number and amount separated by a tab, comma or spaces, with thousands commas", () => {
    expect(parsePastedRows("EMP-00001\t1,500.00\nemp-00002   250\n\n", staff)).toEqual({ rows: [{ employeeId: "1", amount: 1500 }, { employeeId: "2", amount: 250 }], problems: [] });
  });

  it("adds up an employee listed twice and returns what it cannot read", () => {
    const { rows, problems } = parsePastedRows("EMP-00001;100\nEMP-00001;50\nEMP-99999 10\nAbebe overtime", staff);
    expect(rows).toEqual([{ employeeId: "1", amount: 150 }]);
    expect(problems).toEqual(["EMP-99999 10", "Abebe overtime"]);
  });
});
