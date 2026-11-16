import { useMemo, useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatDate, formatMoney, todayIso } from "../../lib/ethiopian";
import { can, type SalesOrderDto } from "../../lib/types";
import { DynamicPage, Fields, FilterBar, FilterField, ListReport, MessageStrip, ObjectPage, ObjectStatus, type Tone } from "../../ui/fiori";
import { currentMonth, monthRange } from "../payroll/month";
import { useParties } from "../accounting/Parties";
import { useItems, useWarehouses } from "../stock/hooks";

const TONE: Record<SalesOrderDto["state"], Tone> = {
  Draft: "critical", ToDeliverAndBill: "information", ToDeliver: "information", ToBill: "information", Completed: "positive", Closed: "neutral", Cancelled: "negative",
};

export function useSalesOrder(id: string | undefined) {
  return useQuery({ queryKey: ["sales-order", id], queryFn: () => api<SalesOrderDto>(`/api/selling/sales-orders/${id}`), enabled: !!id });
}

/** List report of purchase orders with their fulfilment. */
export function SalesOrders() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const navigate = useNavigate();
  const month = monthRange(currentMonth());
  const [from, setFrom] = useState(month.start);
  const [to, setTo] = useState(month.end);
  const [state, setState] = useState("");
  const [search, setSearch] = useState("");
  const orders = useQuery({
    queryKey: ["sales-orders", company?.id, from, to],
    queryFn: () => api<SalesOrderDto[]>(`/api/selling/sales-orders?companyId=${company!.id}&from=${from}&to=${to}`),
    enabled: !!company && from <= to,
  });
  const rows = useMemo(() => (orders.data ?? []).filter((o) => (!state || o.state === state || (state === "open" && ["ToDeliverAndBill", "ToDeliver", "ToBill"].includes(o.state)))
    && (!search.trim() || [o.number, o.customerName, ...o.lines.map((l) => l.itemCode)].some((v) => v?.toLowerCase().includes(search.trim().toLowerCase())))), [orders.data, state, search]);
  if (!company) return <p>{t("common.noCompany")}</p>;
  const am = i18n.language === "am";

  return (
    <ListReport
      title={t("nav.salesOrders")}
      filters={
        <FilterBar>
          <FilterField label={t("ui.search")}><input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t("sell.searchHint")} /></FilterField>
          <FilterField label={t("fields.status")}>
            <select value={state} onChange={(e) => setState(e.target.value)}>
              <option value="">{t("ui.all")}</option><option value="open">{t("inv.open")}</option>
              {(Object.keys(TONE) as SalesOrderDto["state"][]).map((s) => <option key={s} value={s}>{t(`sell.states.${s}`)}</option>)}
            </select>
          </FilterField>
          <FilterField label={t("periods.from")}><input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></FilterField>
          <FilterField label={t("periods.to")}><input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></FilterField>
        </FilterBar>
      }
      tableTitle={t("nav.salesOrders")}
      count={rows.length}
      toolbar={can(me, "selling.orders.create") && <button onClick={() => void navigate({ to: "/selling/orders/new" })}>{t("ui.create")}</button>}
    >
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("fields.number")}</th><th>{t("attendance.date")}</th><th>{t("inv.customer")}</th><th>{t("sell.deliveryDate")}</th>
            <th className="num">{t("inv.net")}</th><th className="num">{t("sell.delivered")}</th><th className="num">{t("sell.billed")}</th><th>{t("fields.status")}</th></tr></thead>
          <tbody>{rows.map((o) => (
            <tr key={o.id} className="navigable" onClick={() => void navigate({ to: "/selling/orders/$id", params: { id: o.id } })}>
              <td data-label={t("fields.number")}><span className="mono strong-link">{o.number}</span></td>
              <td data-label={t("attendance.date")}>{formatDate(o.orderDate, me.calendar, am)}</td>
              <td data-label={t("inv.customer")}>{o.customerName}</td>
              <td data-label={t("sell.deliveryDate")}>{formatDate(o.deliveryDate, me.calendar, am)}</td>
              <td data-label={t("inv.net")} className="num">{formatMoney(o.netTotal)}</td>
              <td data-label={t("sell.delivered")} className="num">{o.status === "Draft" ? "" : `${o.percentDelivered}%`}</td>
              <td data-label={t("sell.billed")} className="num">{o.status === "Draft" ? "" : `${o.percentBilled}%`}</td>
              <td data-label={t("fields.status")}><ObjectStatus tone={TONE[o.state]}>{t(`sell.states.${o.state}`)}</ObjectStatus></td>
            </tr>
          ))}</tbody>
        </table>
      ) : <div className="empty">{orders.isPending ? t("common.loading") : t("common.empty")}</div>}
    </ListReport>
  );
}

type Line = { itemId: string; qty: string; rate: string; warehouseId: string };

/** A new purchase order. */
export function SalesOrderCreate() {
  const { t } = useTranslation();
  const { company } = useSession();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const parties = useParties();
  const items = useItems();
  const warehouses = useWarehouses();
  const [lines, setLines] = useState<Line[]>([{ itemId: "", qty: "", rate: "", warehouseId: "" }]);
  const create = useMutation({
    mutationFn: (body: object) => post<SalesOrderDto>("/api/selling/sales-orders", body),
    onSuccess: (o) => { void queryClient.invalidateQueries({ queryKey: ["sales-orders"] }); void navigate({ to: "/selling/orders/$id", params: { id: o.id } }); },
  });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const setLine = (i: number, patch: Partial<Line>) => setLines((ls) => ls.map((l, j) => (j === i ? { ...l, ...patch } : l)));
  const total = lines.reduce((a, l) => a + Number(l.qty || 0) * Number(l.rate || 0), 0);
  const onSubmit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    create.mutate({
      companyId: company.id, customerId: f.get("customerId"), orderDate: f.get("orderDate"), deliveryDate: f.get("deliveryDate") || null, remark: f.get("remark") || null,
      lines: lines.filter((l) => l.itemId && Number(l.qty) > 0).map((l) => ({ itemId: l.itemId, qty: Number(l.qty), rate: Number(l.rate || 0), warehouseId: l.warehouseId || null })),
    });
  };

  return (
    <DynamicPage
      title={t("sell.newOrder")}
      breadcrumb={<Link to="/selling/orders">{t("nav.salesOrders")}</Link>}
      footer={<>
        {create.isError && <span className="error">{create.error.message}</span>}
        <span className="muted">{t("inv.net")} {formatMoney(total)}</span>
        <button type="submit" form="po-create" disabled={create.isPending || total <= 0}>{t("inv.saveDraft")}</button>
        <button className="secondary" onClick={() => void navigate({ to: "/selling/orders" })}>{t("common.cancel")}</button>
      </>}
    >
      <form id="po-create" onSubmit={onSubmit}>
        <section className="object-section">
          <h2>{t("ui.general")}</h2>
          <div className="form-columns">
            <label>{t("inv.customer")}<span className="required" aria-hidden>*</span>
              <select name="customerId" required defaultValue="">
                <option value="" disabled>—</option>
                {parties.data?.filter((p) => p.type !== "Supplier").map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
              </select>
            </label>
            <label>{t("attendance.date")}<span className="required" aria-hidden>*</span><input type="date" name="orderDate" defaultValue={todayIso()} required /></label>
            <label>{t("sell.deliveryDate")}<input type="date" name="deliveryDate" /></label>
            <label>{t("acc.remark")}<input name="remark" /></label>
          </div>
        </section>
        <section className="object-section">
          <h2>{t("ui.lines")}</h2>
          <table className="table edit-table">
            <thead><tr><th>{t("nav.items")}</th><th className="num">{t("stock.qty")}</th><th className="num">{t("inv.rate")}</th><th>{t("stock.from")}</th><th className="num">{t("cash.amount")}</th><th /></tr></thead>
            <tbody>{lines.map((l, i) => (
              <tr key={i}>
                <td><select aria-label={t("nav.items")} value={l.itemId} onChange={(e) => setLine(i, { itemId: e.target.value })} required={i === 0}>
                  <option value="">—</option>{items.data?.filter((x) => x.isActive).map((x) => <option key={x.id} value={x.id}>{x.code} · {x.name} ({x.uom})</option>)}
                </select></td>
                <td><input aria-label={t("stock.qty")} className="num" type="number" step="0.001" min="0" value={l.qty} onChange={(e) => setLine(i, { qty: e.target.value })} /></td>
                <td><input aria-label={t("inv.rate")} className="num" type="number" step="0.01" min="0" value={l.rate} onChange={(e) => setLine(i, { rate: e.target.value })} /></td>
                <td><select aria-label={t("stock.from")} value={l.warehouseId} onChange={(e) => setLine(i, { warehouseId: e.target.value })}>
                  <option value="">—</option>{warehouses.data?.map((w) => <option key={w.id} value={w.id}>{w.code}</option>)}
                </select></td>
                <td className="num">{formatMoney(Number(l.qty || 0) * Number(l.rate || 0))}</td>
                <td>{lines.length > 1 && <button type="button" className="link-button" aria-label={t("ui.remove")} onClick={() => setLines((ls) => ls.filter((_, j) => j !== i))}>×</button>}</td>
              </tr>
            ))}</tbody>
            <tfoot><tr><td colSpan={4}><button type="button" className="secondary" onClick={() => setLines((ls) => [...ls, { itemId: "", qty: "", rate: "", warehouseId: "" }])}>{t("acc.addLine")}</button></td>
              <td className="num">{formatMoney(total)}</td><td /></tr></tfoot>
          </table>
        </section>
      </form>
    </DynamicPage>
  );
}

/** Object page of a purchase order: fulfilment per line, and the actions that continue it in Stock and Accounting. */
export function SalesOrderDetail() {
  const { t, i18n } = useTranslation();
  const { me, modules } = useSession();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { id } = useParams({ from: "/selling/orders/$id" });
  const [notice, setNotice] = useState<string | null>(null);
  const order = useSalesOrder(id);
  const act = useMutation({
    mutationFn: (action: "submit" | "cancel" | "close" | "reopen") => post<SalesOrderDto>(`/api/selling/sales-orders/${id}/${action}`),
    onSuccess: (o) => { queryClient.setQueryData(["sales-order", id], o); void queryClient.invalidateQueries({ queryKey: ["sales-orders"] }); setNotice(t(`sell.states.${o.state}`)); },
  });
  if (order.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (order.isError) return <MessageStrip tone="negative">{order.error.message}</MessageStrip>;
  const o = order.data;
  const am = i18n.language === "am";
  const open = ["ToDeliverAndBill", "ToDeliver", "ToBill"].includes(o.state);
  const has = (m: string) => modules.some((x) => x.id === m);

  return (
    <ObjectPage
      breadcrumb={<Link to="/selling/orders">{t("nav.salesOrders")}</Link>}
      title={o.number ?? t("status.Draft")}
      subtitle={o.customerName}
      status={<ObjectStatus tone={TONE[o.state]}>{t(`sell.states.${o.state}`)}</ObjectStatus>}
      kpis={[
        { label: t("inv.net"), value: formatMoney(o.netTotal) },
        { label: t("sell.delivered"), value: `${o.percentDelivered}%`, tone: o.percentDelivered >= 100 ? "positive" : undefined },
        { label: t("sell.billed"), value: `${o.percentBilled}%`, tone: o.percentBilled >= 100 ? "positive" : undefined },
        { label: t("sell.deliveryDate"), value: formatDate(o.deliveryDate, me.calendar, am), tone: open && o.deliveryDate < todayIso() && o.percentDelivered < 100 ? "negative" : undefined },
      ]}
      actions={<>
        {o.status === "Draft" && can(me, "selling.orders.submit") && <button onClick={() => act.mutate("submit")} disabled={act.isPending}>{t("slips.submit")}</button>}
        {open && o.percentDelivered < 100 && has("stock") && can(me, "stock.entries.create") &&
          <button onClick={() => void navigate({ to: "/stock/entries/new", search: { purpose: "Issue", so: o.id } })}>{t("sell.deliver")}</button>}
        {open && o.percentBilled < 100 && has("accounting") && can(me, "accounting.invoices.create") &&
          <button className="secondary" onClick={() => void navigate({ to: "/accounting/invoices/new", search: { kind: "Sales", so: o.id } })}>{t("sell.bill")}</button>}
        {open && can(me, "selling.orders.submit") && <button className="secondary" onClick={() => act.mutate("close")} disabled={act.isPending}>{t("sell.close")}</button>}
        {o.state === "Closed" && can(me, "selling.orders.submit") && <button className="secondary" onClick={() => act.mutate("reopen")} disabled={act.isPending}>{t("sell.reopen")}</button>}
        {o.status === "Submitted" && o.percentDelivered === 0 && o.percentBilled === 0 && can(me, "selling.orders.submit") &&
          <button className="secondary" onClick={() => act.mutate("cancel")} disabled={act.isPending}>{t("common.cancel")}</button>}
      </>}
      messages={<>
        {notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
        {act.isError && <MessageStrip tone="negative">{act.error.message}</MessageStrip>}
        {o.status === "Draft" && <MessageStrip tone="critical">{t("sell.draftHint")}</MessageStrip>}
      </>}
      sections={[
        { id: "lines", title: t("ui.lines"), content: (
          <table className="table">
            <thead><tr><th>{t("nav.items")}</th><th className="num">{t("stock.qty")}</th><th className="num">{t("inv.rate")}</th><th className="num">{t("cash.amount")}</th>
              <th className="num">{t("sell.delivered")}</th><th className="num">{t("sell.billed")}</th></tr></thead>
            <tbody>{o.lines.map((l) => (
              <tr key={l.itemId}>
                <td><span className="mono">{l.itemCode}</span> · {l.itemName}</td><td className="num">{l.qty} {l.uom}</td><td className="num">{formatMoney(l.rate)}</td>
                <td className="num">{formatMoney(l.amount)}</td>
                <td className="num"><ObjectStatus tone={l.deliveredQty >= l.qty ? "positive" : "neutral"}>{l.deliveredQty}</ObjectStatus></td>
                <td className="num"><ObjectStatus tone={l.billedQty >= l.qty ? "positive" : "neutral"}>{l.billedQty}</ObjectStatus></td>
              </tr>
            ))}</tbody>
            <tfoot><tr><td colSpan={3}>{t("common.total")}</td><td className="num">{formatMoney(o.netTotal)}</td><td /><td /></tr></tfoot>
          </table>
        ) },
        { id: "general", title: t("ui.general"), content: (
          <Fields items={[
            { label: t("inv.customer"), value: o.customerName },
            { label: t("attendance.date"), value: formatDate(o.orderDate, me.calendar, am) },
            { label: t("sell.deliveryDate"), value: formatDate(o.deliveryDate, me.calendar, am) },
            { label: t("acc.remark"), value: o.remark },
          ]} />
        ) },
      ]}
    />
  );
}
