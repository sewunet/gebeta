import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api } from "../../lib/api";
import { formatDate, formatEthiopian, formatMoney, todayIso } from "../../lib/ethiopian";
import type { StockBalanceDto, StockLedgerDto } from "../../lib/types";
import { DynamicPage, FilterBar, FilterField } from "../../ui/fiori";
import { currentMonth, monthRange } from "../payroll/month";
import { useItems, useWarehouses } from "./hooks";

/** Quantity and value of every item in every warehouse on a date. */
export function StockBalance() {
  const { t, i18n } = useTranslation();
  const { company } = useSession();
  const navigate = useNavigate();
  const [asOf, setAsOf] = useState(todayIso());
  const [warehouse, setWarehouse] = useState("");
  const [search, setSearch] = useState("");
  const warehouses = useWarehouses();
  const balance = useQuery({
    queryKey: ["stock-balance", company?.id, asOf],
    queryFn: () => api<StockBalanceDto>(`/api/stock/reports/balance?companyId=${company!.id}&asOf=${asOf}`),
    enabled: !!company,
  });
  const rows = useMemo(() => (balance.data?.rows ?? []).filter((r) => (!warehouse || r.warehouseId === warehouse)
    && (!search.trim() || `${r.itemCode} ${r.itemName}`.toLowerCase().includes(search.trim().toLowerCase()))), [balance.data, warehouse, search]);
  if (!company) return <p>{t("common.noCompany")}</p>;
  const total = rows.reduce((a, r) => a + r.value, 0);

  return (
    <DynamicPage
      title={t("nav.stockBalance")}
      subtitle={<>{company.name} · {formatEthiopian(asOf, i18n.language === "am")}</>}
      header={<>
        <FilterBar>
          <FilterField label={t("ui.search")}><input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t("acc.accountSearchHint")} /></FilterField>
          <FilterField label={t("nav.warehouses")}>
            <select value={warehouse} onChange={(e) => setWarehouse(e.target.value)}>
              <option value="">{t("ui.all")}</option>{warehouses.data?.map((w) => <option key={w.id} value={w.id}>{w.code} · {w.name}</option>)}
            </select>
          </FilterField>
          <FilterField label={t("acc.asOf")}><input type="date" value={asOf} onChange={(e) => setAsOf(e.target.value)} /></FilterField>
        </FilterBar>
        <div className="kpis kpis-gap">
          <div className="kpi"><div className="kpi-label">{t("stock.value")}</div><div className="kpi-value">{formatMoney(total)}</div></div>
          <div className="kpi"><div className="kpi-label">{t("nav.items")}</div><div className="kpi-value">{new Set(rows.map((r) => r.itemId)).size}</div></div>
        </div>
      </>}
    >
      <section className="table-card">
        {rows.length ? (
          <table className="table responsive">
            <thead><tr><th>{t("acc.code")}</th><th>{t("fields.name")}</th><th>{t("nav.warehouses")}</th><th className="num">{t("stock.qty")}</th><th className="num">{t("inv.rate")}</th>
              <th className="num">{t("stock.value")}</th><th>{t("stock.method")}</th></tr></thead>
            <tbody>{rows.map((r) => (
              <tr key={`${r.itemId}-${r.warehouseId}`} className="navigable" onClick={() => void navigate({ to: "/stock/ledger", search: { item: r.itemId } })}>
                <td data-label={t("acc.code")} className="mono strong-link">{r.itemCode}</td><td data-label={t("fields.name")}>{r.itemName}</td>
                <td data-label={t("nav.warehouses")} className="mono">{r.warehouse}</td>
                <td data-label={t("stock.qty")} className="num">{r.qty} {r.uom}</td><td data-label={t("inv.rate")} className="num">{formatMoney(r.rate)}</td>
                <td data-label={t("stock.value")} className="num">{formatMoney(r.value)}</td><td data-label={t("stock.method")}>{t(`stock.methods.${r.method}`)}</td>
              </tr>
            ))}</tbody>
            <tfoot><tr><td colSpan={5}>{t("common.total")}</td><td className="num">{formatMoney(total)}</td><td /></tr></tfoot>
          </table>
        ) : <div className="empty">{balance.isPending ? t("common.loading") : t("stock.noStock")}</div>}
      </section>
    </DynamicPage>
  );
}

/** Every movement of one item, with the running quantity and value. */
export function StockLedger() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const navigate = useNavigate();
  const { item } = useSearch({ from: "/stock/ledger" });
  const month = monthRange(currentMonth());
  const [from, setFrom] = useState(month.start);
  const [to, setTo] = useState(todayIso());
  const [warehouse, setWarehouse] = useState("");
  const items = useItems();
  const warehouses = useWarehouses();
  const itemId = item ?? items.data?.find((i) => i.isStockItem)?.id;
  const ledger = useQuery({
    queryKey: ["stock-ledger", company?.id, itemId, warehouse, from, to],
    queryFn: () => api<StockLedgerDto>(`/api/stock/reports/ledger?companyId=${company!.id}&itemId=${itemId}${warehouse ? `&warehouseId=${warehouse}` : ""}&from=${from}&to=${to}`),
    enabled: !!company && !!itemId && from <= to,
  });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const am = i18n.language === "am";

  return (
    <DynamicPage
      title={t("nav.stockLedger")}
      subtitle={ledger.data && <><span className="mono">{ledger.data.itemCode}</span> · {ledger.data.itemName}</>}
      header={
        <FilterBar>
          <FilterField label={t("nav.items")}>
            <select value={itemId ?? ""} onChange={(e) => void navigate({ to: "/stock/ledger", search: { item: e.target.value } })}>
              {items.data?.filter((i) => i.isStockItem).map((i) => <option key={i.id} value={i.id}>{i.code} · {i.name}</option>)}
            </select>
          </FilterField>
          <FilterField label={t("nav.warehouses")}>
            <select value={warehouse} onChange={(e) => setWarehouse(e.target.value)}>
              <option value="">{t("ui.all")}</option>{warehouses.data?.map((w) => <option key={w.id} value={w.id}>{w.code}</option>)}
            </select>
          </FilterField>
          <FilterField label={t("periods.from")}><input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></FilterField>
          <FilterField label={t("periods.to")}><input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></FilterField>
        </FilterBar>
      }
    >
      <section className="table-card">
        {ledger.data?.rows.length ? (
          <table className="table">
            <thead><tr><th>{t("attendance.date")}</th><th>{t("reports.voucher")}</th><th>{t("nav.warehouses")}</th><th className="num">{t("stock.qty")}</th>
              <th className="num">{t("inv.rate")}</th><th className="num">{t("stock.valueChange")}</th><th className="num">{t("stock.qtyAfter")}</th><th className="num">{t("stock.valueAfter")}</th></tr></thead>
            <tbody>{ledger.data.rows.map((r, i) => (
              <tr key={i}>
                <td>{formatDate(r.date, me.calendar, am)}</td><td className="mono">{r.voucher}</td><td className="mono">{r.warehouse}</td>
                <td className={`num ${r.qty < 0 ? "neg" : ""}`}>{r.qty}</td><td className="num">{formatMoney(r.rate)}</td>
                <td className={`num ${r.valueChange < 0 ? "neg" : ""}`}>{formatMoney(r.valueChange)}</td><td className="num">{r.qtyAfter}</td><td className="num">{formatMoney(r.valueAfter)}</td>
              </tr>
            ))}</tbody>
          </table>
        ) : <div className="empty">{ledger.isPending && itemId ? t("common.loading") : t("common.empty")}</div>}
      </section>
    </DynamicPage>
  );
}
