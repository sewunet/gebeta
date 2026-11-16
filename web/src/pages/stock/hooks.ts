import { useQuery } from "@tanstack/react-query";
import { useSession } from "../../app/context";
import { api } from "../../lib/api";
import { can, type ItemDto, type WarehouseDto } from "../../lib/types";

export function useItems() {
  const { company } = useSession();
  return useQuery({ queryKey: ["items", company?.id], queryFn: () => api<ItemDto[]>(`/api/core/items?companyId=${company!.id}`), enabled: !!company });
}

export function useWarehouses() {
  const { company, me, modules } = useSession();
  return useQuery({
    queryKey: ["warehouses", company?.id],
    queryFn: () => api<WarehouseDto[]>(`/api/stock/warehouses?companyId=${company!.id}`),
    enabled: !!company && modules.some((m) => m.id === "stock") && can(me, "stock.warehouses.read"),
  });
}
