import { useQuery } from "@tanstack/react-query";
import { useSession } from "../app/context";
import { api } from "./api";
import type { EmployeeDto } from "./types";

/** Employees of the selected company (shared by HR and Payroll screens). */
export function useEmployees() {
  const { company } = useSession();
  return useQuery({
    queryKey: ["employees", company?.id],
    queryFn: () => api<EmployeeDto[]>(`/api/core/employees?companyId=${company!.id}`),
    enabled: !!company,
  });
}

/** Form fields as an object. */
export const formData = (form: HTMLFormElement) => Object.fromEntries(new FormData(form)) as Record<string, string>;
