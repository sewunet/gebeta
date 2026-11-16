import { useMemo, useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatMoney } from "../../lib/ethiopian";
import { formData } from "../../lib/hooks";
import { can, type CashierDto } from "../../lib/types";
import { Dialog, FilterBar, FilterField, ListReport, MessageStrip, ObjectStatus, statusTone } from "../../ui/fiori";

export function useCashiers() {
  const { company } = useSession();
  return useQuery({
    queryKey: ["cashiers", company?.id],
    queryFn: () => api<CashierDto[]>(`/api/cash/cashiers?companyId=${company!.id}`),
    enabled: !!company,
  });
}

/** List report of cash desks with their balances; a row opens the cashier. */
export function Cashiers() {
  const { t } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [adding, setAdding] = useState(false);
  const [type, setType] = useState("");
  const [search, setSearch] = useState("");
  const cashiers = useCashiers();
  const add = useMutation({
    mutationFn: (b: Record<string, string>) => post<CashierDto>("/api/cash/cashiers", { ...b, companyId: company!.id, autoTopUp: b.autoTopUp === "on" }),
    onSuccess: (c) => { setAdding(false); void queryClient.invalidateQueries({ queryKey: ["cashiers"] }); void navigate({ to: "/cash/cashiers/$id", params: { id: c.id } }); },
  });
  const rows = useMemo(() => (cashiers.data ?? []).filter((c) => (!type || c.type === type)
    && (!search.trim() || `${c.code} ${c.name}`.toLowerCase().includes(search.trim().toLowerCase()))), [cashiers.data, type, search]);
  if (!company) return <p>{t("common.noCompany")}</p>;
  const onAdd = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); add.mutate(formData(e.currentTarget)); };

  return (
    <ListReport
      title={t("nav.cashiers")}
      filters={
        <FilterBar>
          <FilterField label={t("ui.search")}><input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t("cash.searchHint")} /></FilterField>
          <FilterField label={t("fields.type")}>
            <select value={type} onChange={(e) => setType(e.target.value)}>
              <option value="">{t("ui.all")}</option>
              {["SalesCashier", "PettyCashier", "Other"].map((x) => <option key={x} value={x}>{t(`cash.types.${x}`)}</option>)}
            </select>
          </FilterField>
        </FilterBar>
      }
      tableTitle={t("nav.cashiers")}
      count={rows.length}
      toolbar={can(me, "cash.cashiers.manage") && <button onClick={() => setAdding(true)}>{t("ui.create")}</button>}
    >
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("cash.code")}</th><th>{t("fields.name")}</th><th>{t("fields.type")}</th><th>{t("fields.status")}</th><th className="num">{t("cash.balance")}</th></tr></thead>
          <tbody>
            {rows.map((c) => (
              <tr key={c.id} className="navigable" onClick={() => void navigate({ to: "/cash/cashiers/$id", params: { id: c.id } })}>
                <td data-label={t("cash.code")}><span className="mono strong-link">{c.code}</span></td>
                <td data-label={t("fields.name")}>{c.name}</td>
                <td data-label={t("fields.type")}>{t(`cash.types.${c.type}`)}</td>
                <td data-label={t("fields.status")}><ObjectStatus tone={statusTone(c.status)}>{t(`status.${c.status}`, c.status)}</ObjectStatus></td>
                <td data-label={t("cash.balance")} className="num strong">{formatMoney(c.balance)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot><tr><td colSpan={4}>{t("common.total")}</td><td className="num">{formatMoney(rows.reduce((a, c) => a + c.balance, 0))}</td></tr></tfoot>
        </table>
      ) : <div className="empty">{cashiers.isPending ? t("common.loading") : t("common.empty")}</div>}

      <Dialog open={adding} title={t("cash.addCashier")} onClose={() => setAdding(false)}
        footer={<><button type="submit" form="cashier-form" disabled={add.isPending}>{t("common.save")}</button><button className="secondary" onClick={() => setAdding(false)}>{t("common.cancel")}</button></>}>
        <form id="cashier-form" className="dialog-form" onSubmit={onAdd}>
          <label>{t("cash.code")}<span className="required" aria-hidden>*</span><input name="code" required maxLength={20} /></label>
          <label>{t("fields.name")}<span className="required" aria-hidden>*</span><input name="name" required /></label>
          <label>{t("fields.type")}<select name="type">{["SalesCashier", "PettyCashier", "Other"].map((x) => <option key={x} value={x}>{t(`cash.types.${x}`)}</option>)}</select></label>
          <label className="check"><input type="checkbox" name="autoTopUp" /> {t("cash.autoTopUp")}</label>
          {add.isError && <MessageStrip tone="negative">{add.error.message}</MessageStrip>}
        </form>
      </Dialog>
    </ListReport>
  );
}
