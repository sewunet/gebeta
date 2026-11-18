/**
 * Data hooks shared across screens. Screens import data from here, never from each other's page files, so a form in one
 * area does not pull another area's screens into its bundle.
 */
import { useQuery } from "@tanstack/react-query";
import { useSession } from "../app/context";
import { api } from "./api";
import { can, type BankAccountDto, type CashierDto, type ItemDto, type PartyDto, type PurchaseOrderDto, type RoleDto, type SalaryStructureDto, type SalesOrderDto, type UserDto, type WarehouseDto } from "./types";

/** Customers and suppliers of the selected company (kept in Core). */
export function useParties() {
  const { company } = useSession();
  return useQuery({ queryKey: ["parties", company?.id], queryFn: () => api<PartyDto[]>(`/api/core/parties?companyId=${company!.id}`), enabled: !!company });
}

/** Items of the selected company (kept in Core). */
export function useItems() {
  const { company } = useSession();
  return useQuery({ queryKey: ["items", company?.id], queryFn: () => api<ItemDto[]>(`/api/core/items?companyId=${company!.id}`), enabled: !!company });
}

/** Warehouses, when Stock is installed and the user may see them. */
export function useWarehouses() {
  const { company, me, modules } = useSession();
  return useQuery({
    queryKey: ["warehouses", company?.id],
    queryFn: () => api<WarehouseDto[]>(`/api/stock/warehouses?companyId=${company!.id}`),
    enabled: !!company && modules.some((m) => m.id === "stock") && can(me, "stock.warehouses.read"),
  });
}

export function useBankAccounts() {
  const { company } = useSession();
  return useQuery({
    queryKey: ["bank-accounts", company?.id],
    queryFn: () => api<BankAccountDto[]>(`/api/accounting/bank-accounts?companyId=${company!.id}`),
    enabled: !!company,
  });
}

export function useCashiers() {
  const { company } = useSession();
  return useQuery({ queryKey: ["cashiers", company?.id], queryFn: () => api<CashierDto[]>(`/api/cash/cashiers?companyId=${company!.id}`), enabled: !!company });
}

export function usePurchaseOrder(id: string | undefined) {
  return useQuery({ queryKey: ["purchase-order", id], queryFn: () => api<PurchaseOrderDto>(`/api/buying/purchase-orders/${id}`), enabled: !!id });
}

export function useSalesOrder(id: string | undefined) {
  return useQuery({ queryKey: ["sales-order", id], queryFn: () => api<SalesOrderDto>(`/api/selling/sales-orders/${id}`), enabled: !!id });
}

/** Salary structures of the selected company, when Payroll is installed and the user may see them. */
export function useSalaryStructures() {
  const { company, me, modules } = useSession();
  return useQuery({
    queryKey: ["salary-structures", company?.id],
    queryFn: () => api<SalaryStructureDto[]>(`/api/payroll/structures?companyId=${company!.id}`),
    enabled: !!company && modules.some((m) => m.id === "payroll") && can(me, "payroll.structures.read"),
  });
}

/** Every user (users are not per company; their company access is a list on each). */
export function useUsers() {
  const { me } = useSession();
  return useQuery({ queryKey: ["users"], queryFn: () => api<UserDto[]>("/api/core/users"), enabled: can(me, "core.users.read") });
}

export function useRoles() {
  const { me } = useSession();
  return useQuery({ queryKey: ["roles"], queryFn: () => api<RoleDto[]>("/api/core/roles"), enabled: can(me, "core.roles.read") || can(me, "core.users.read") });
}

/** A role that lets its users see every company (their company list then does not matter). */
export const seesAllCompanies = (role: RoleDto | undefined) => !!role?.permissions.some((p) => p === "*" || p === "core.*" || p === "core.companies.access-all");
