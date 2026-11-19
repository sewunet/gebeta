import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams, useSearch } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatDate, formatMoney, todayIso } from "../../lib/ethiopian";
import { can, type PurchaseOrderDto } from "../../lib/types";
import { DynamicPage, Fields, FilterBar, FilterField, ListReport, MessageStrip, ObjectPage, ObjectStatus, type Tone } from "../../ui/fiori";
import { currentMonth, monthRange } from "../payroll/month";
import { useBlanketAgreement, useItems, useParties, usePurchaseOrder, useWarehouses } from "../../lib/queries";
import { historySection } from "../../app/history";
import { attachmentsSection } from "../../app/attachments";
import { relatedSection } from "../../app/related";

const TONE: Record<PurchaseOrderDto["state"], Tone> = {
  Draft: "critical", ToReceiveAndBill: "information", ToReceive: "information", ToBill: "information", Completed: "positive", Closed: "neutral", Cancelled: "negative",
};

/** List report of purchase orders with their fulfilment. */
export function PurchaseOrders() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const navigate = useNavigate();
  const month = monthRange(currentMonth());
  const [from, setFrom] = useState(month.start);
  const [to, setTo] = useState(month.end);
  const [state, setState] = useState("");
  const [search, setSearch] = useState("");
  const orders = useQuery({
    queryKey: ["purchase-orders", company?.id, from, to],
    queryFn: () => api<PurchaseOrderDto[]>(`/api/buying/purchase-orders?companyId=${company!.id}&from=${from}&to=${to}`),
    enabled: !!company && from <= to,
  });
  const rows = useMemo(() => (orders.data ?? []).filter((o) => (!state || o.state === state || (state === "open" && ["ToReceiveAndBill", "ToReceive", "ToBill"].includes(o.state)))
    && (!search.trim() || [o.number, o.supplierName, ...o.lines.map((l) => l.itemCode)].some((v) => v?.toLowerCase().includes(search.trim().toLowerCase())))), [orders.data, state, search]);
  if (!company) return <p>{t("common.noCompany")}</p>;
  const am = i18n.language === "am";

  return (
    <ListReport
      title={t("nav.purchaseOrders")}
      filters={
        <FilterBar>
          <FilterField label={t("ui.search")}><input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t("buy.searchHint")} /></FilterField>
          <FilterField label={t("fields.status")}>
            <select value={state} onChange={(e) => setState(e.target.value)}>
              <option value="">{t("ui.all")}</option><option value="open">{t("inv.open")}</option>
              {(Object.keys(TONE) as PurchaseOrderDto["state"][]).map((s) => <option key={s} value={s}>{t(`buy.states.${s}`)}</option>)}
            </select>
          </FilterField>
          <FilterField label={t("periods.from")}><input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></FilterField>
          <FilterField label={t("periods.to")}><input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></FilterField>
        </FilterBar>
      }
      tableTitle={t("nav.purchaseOrders")}
      count={rows.length}
      toolbar={can(me, "buying.orders.create") && <button onClick={() => void navigate({ to: "/buying/orders/new" })}>{t("ui.create")}</button>}
    >
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("fields.number")}</th><th>{t("attendance.date")}</th><th>{t("inv.supplier")}</th><th>{t("buy.requiredBy")}</th>
            <th className="num">{t("inv.net")}</th><th className="num">{t("buy.received")}</th><th className="num">{t("buy.billed")}</th><th>{t("fields.status")}</th></tr></thead>
          <tbody>{rows.map((o) => (
            <tr key={o.id} className="navigable" onClick={() => void navigate({ to: "/buying/orders/$id", params: { id: o.id } })}>
              <td data-label={t("fields.number")}><span className="mono strong-link">{o.number}</span></td>
              <td data-label={t("attendance.date")}>{formatDate(o.orderDate, me.calendar, am)}</td>
              <td data-label={t("inv.supplier")}>{o.supplierName}</td>
              <td data-label={t("buy.requiredBy")}>{formatDate(o.requiredBy, me.calendar, am)}</td>
              <td data-label={t("inv.net")} className="num">{formatMoney(o.netTotal)}</td>
              <td data-label={t("buy.received")} className="num">{o.status === "Draft" ? "" : `${o.percentReceived}%`}</td>
              <td data-label={t("buy.billed")} className="num">{o.status === "Draft" ? "" : `${o.percentBilled}%`}</td>
              <td data-label={t("fields.status")}><ObjectStatus tone={TONE[o.state]}>{t(`buy.states.${o.state}`)}</ObjectStatus></td>
            </tr>
          ))}</tbody>
        </table>
      ) : <div className="empty">{orders.isPending ? t("common.loading") : t("common.empty")}</div>}
    </ListReport>
  );
}

type Line = { itemId: string; qty: string; rate: string; warehouseId: string };

/** A new purchase order. */
export function PurchaseOrderCreate() {
  const { t } = useTranslation();
  const { company } = useSession();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const parties = useParties();
  const items = useItems();
  const warehouses = useWarehouses();
  const { agreement: agreementId } = useSearch({ from: "/buying/orders/new" });
  const agreement = useBlanketAgreement(agreementId);
  const [lines, setLines] = useState<Line[]>([{ itemId: "", qty: "", rate: "", warehouseId: "" }]);
  // Calling off an agreement: its items come with their agreed rates; the buyer enters the quantities.
  const agreedId = agreement.data?.id;
  useEffect(() => {
    if (agreement.data) setLines(agreement.data.lines.map((l) => ({ itemId: l.itemId, qty: "", rate: String(l.rate), warehouseId: "" })));
  }, [agreedId]); // eslint-disable-line react-hooks/exhaustive-deps
  const create = useMutation({
    mutationFn: (body: object) => post<PurchaseOrderDto>("/api/buying/purchase-orders", body),
    onSuccess: (o) => { void queryClient.invalidateQueries({ queryKey: ["purchase-orders"] }); void navigate({ to: "/buying/orders/$id", params: { id: o.id } }); },
  });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const setLine = (i: number, patch: Partial<Line>) => setLines((ls) => ls.map((l, j) => (j === i ? { ...l, ...patch } : l)));
  const total = lines.reduce((a, l) => a + Number(l.qty || 0) * Number(l.rate || 0), 0);
  const onSubmit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    create.mutate({
      companyId: company.id, supplierId: f.get("supplierId"), orderDate: f.get("orderDate"), requiredBy: f.get("requiredBy") || null, remark: f.get("remark") || null, agreementId: agreementId ?? null,
      lines: lines.filter((l) => l.itemId && Number(l.qty) > 0).map((l) => ({ itemId: l.itemId, qty: Number(l.qty), rate: Number(l.rate || 0), warehouseId: l.warehouseId || null })),
    });
  };

  return (
    <DynamicPage
      title={t("buy.newOrder")}
      breadcrumb={<Link to="/buying/orders">{t("nav.purchaseOrders")}</Link>}
      footer={<>
        {create.isError && <span className="error">{create.error.message}</span>}
        <span className="muted">{t("inv.net")} {formatMoney(total)}</span>
        <button type="submit" form="po-create" disabled={create.isPending || total <= 0}>{t("inv.saveDraft")}</button>
        <button className="secondary" onClick={() => void navigate({ to: "/buying/orders" })}>{t("common.cancel")}</button>
      </>}
    >
      <form id="po-create" onSubmit={onSubmit}>
        <section className="object-section">
          <h2>{t("ui.general")}</h2>
          <div className="form-columns">
            <label>{t("inv.supplier")}<span className="required" aria-hidden>*</span>
              <select name="supplierId" required defaultValue={agreement.data?.supplierId ?? ""} key={agreedId ?? "free"}>
                <option value="" disabled>—</option>
                {parties.data?.filter((p) => p.type !== "Customer").map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
              </select>
            </label>
            <label>{t("attendance.date")}<span className="required" aria-hidden>*</span><input type="date" name="orderDate" defaultValue={todayIso()} required /></label>
            <label>{t("buy.requiredBy")}<input type="date" name="requiredBy" /></label>
            <label>{t("acc.remark")}<input name="remark" /></label>
          </div>
        </section>
        <section className="object-section">
          <h2>{t("ui.lines")}</h2>
          {agreement.data && <MessageStrip tone="information">{t("buy.agr.agreement")} {agreement.data.number}: {t("buy.agr.orderHint")}</MessageStrip>}
          <table className="table edit-table">
            <thead><tr><th>{t("nav.items")}</th><th className="num">{t("stock.qty")}</th><th className="num">{t("inv.rate")}</th><th>{t("stock.to")}</th><th className="num">{t("cash.amount")}</th><th /></tr></thead>
            <tbody>{lines.map((l, i) => (
              <tr key={i}>
                <td><select aria-label={t("nav.items")} value={l.itemId} onChange={(e) => setLine(i, { itemId: e.target.value })} required={i === 0}>
                  <option value="">—</option>{items.data?.filter((x) => x.isActive).map((x) => <option key={x.id} value={x.id}>{x.code} · {x.name} ({x.uom})</option>)}
                </select></td>
                <td><input aria-label={t("stock.qty")} className="num" type="number" step="0.001" min="0" value={l.qty} onChange={(e) => setLine(i, { qty: e.target.value })} /></td>
                <td><input aria-label={t("inv.rate")} className="num" type="number" step="0.01" min="0" value={l.rate} onChange={(e) => setLine(i, { rate: e.target.value })} /></td>
                <td><select aria-label={t("stock.to")} value={l.warehouseId} onChange={(e) => setLine(i, { warehouseId: e.target.value })}>
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
export function PurchaseOrderDetail() {
  const { t, i18n } = useTranslation();
  const { me, modules } = useSession();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { id } = useParams({ from: "/buying/orders/$id" });
  const [notice, setNotice] = useState<string | null>(null);
  const order = usePurchaseOrder(id);
  const act = useMutation({
    mutationFn: (action: "submit" | "cancel" | "close" | "reopen") => post<PurchaseOrderDto>(`/api/buying/purchase-orders/${id}/${action}`),
    onSuccess: (o) => { queryClient.setQueryData(["purchase-order", id], o); void queryClient.invalidateQueries({ queryKey: ["purchase-orders"] }); setNotice(t(`buy.states.${o.state}`)); },
  });
  if (order.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (order.isError) return <MessageStrip tone="negative">{order.error.message}</MessageStrip>;
  const o = order.data;
  const am = i18n.language === "am";
  const open = ["ToReceiveAndBill", "ToReceive", "ToBill"].includes(o.state);
  const has = (m: string) => modules.some((x) => x.id === m);

  return (
    <ObjectPage
      breadcrumb={<Link to="/buying/orders">{t("nav.purchaseOrders")}</Link>}
      title={o.number ?? t("status.Draft")}
      subtitle={o.supplierName}
      status={<ObjectStatus tone={TONE[o.state]}>{t(`buy.states.${o.state}`)}</ObjectStatus>}
      kpis={[
        { label: t("inv.net"), value: formatMoney(o.netTotal) },
        { label: t("buy.received"), value: `${o.percentReceived}%`, tone: o.percentReceived >= 100 ? "positive" : undefined },
        { label: t("buy.billed"), value: `${o.percentBilled}%`, tone: o.percentBilled >= 100 ? "positive" : undefined },
        { label: t("buy.requiredBy"), value: formatDate(o.requiredBy, me.calendar, am), tone: open && o.requiredBy < todayIso() && o.percentReceived < 100 ? "negative" : undefined },
      ]}
      actions={<>
        {o.status === "Draft" && can(me, "buying.orders.submit") && <button onClick={() => act.mutate("submit")} disabled={act.isPending}>{t("slips.submit")}</button>}
        {open && o.percentReceived < 100 && has("stock") && can(me, "stock.entries.create") &&
          <button onClick={() => void navigate({ to: "/stock/entries/new", search: { purpose: "Receipt", po: o.id } })}>{t("buy.receive")}</button>}
        {open && o.percentBilled < 100 && has("accounting") && can(me, "accounting.invoices.create") &&
          <button className="secondary" onClick={() => void navigate({ to: "/accounting/invoices/new", search: { kind: "Purchase", po: o.id } })}>{t("buy.bill")}</button>}
        {open && can(me, "buying.orders.submit") && <button className="secondary" onClick={() => act.mutate("close")} disabled={act.isPending}>{t("buy.close")}</button>}
        {o.state === "Closed" && can(me, "buying.orders.submit") && <button className="secondary" onClick={() => act.mutate("reopen")} disabled={act.isPending}>{t("buy.reopen")}</button>}
        {o.status === "Submitted" && o.percentReceived === 0 && o.percentBilled === 0 && can(me, "buying.orders.submit") &&
          <button className="secondary" onClick={() => act.mutate("cancel")} disabled={act.isPending}>{t("common.cancel")}</button>}
      </>}
      messages={<>
        {notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
        {act.isError && <MessageStrip tone="negative">{act.error.message}</MessageStrip>}
        {o.status === "Draft" && <MessageStrip tone="critical">{t("buy.draftHint")}</MessageStrip>}
      </>}
      sections={[
        { id: "lines", title: t("ui.lines"), content: (
          <table className="table">
            <thead><tr><th>{t("nav.items")}</th><th className="num">{t("stock.qty")}</th><th className="num">{t("inv.rate")}</th><th className="num">{t("cash.amount")}</th>
              <th className="num">{t("buy.received")}</th><th className="num">{t("buy.billed")}</th></tr></thead>
            <tbody>{o.lines.map((l) => (
              <tr key={l.itemId}>
                <td><span className="mono">{l.itemCode}</span> · {l.itemName}</td><td className="num">{l.qty} {l.uom}</td><td className="num">{formatMoney(l.rate)}</td>
                <td className="num">{formatMoney(l.amount)}</td>
                <td className="num"><ObjectStatus tone={l.receivedQty >= l.qty ? "positive" : "neutral"}>{l.receivedQty}</ObjectStatus></td>
                <td className="num"><ObjectStatus tone={l.billedQty >= l.qty ? "positive" : "neutral"}>{l.billedQty}</ObjectStatus></td>
              </tr>
            ))}</tbody>
            <tfoot><tr><td colSpan={3}>{t("common.total")}</td><td className="num">{formatMoney(o.netTotal)}</td><td /><td /></tr></tfoot>
          </table>
        ) },
        { id: "general", title: t("ui.general"), content: (
          <Fields items={[
            { label: t("inv.supplier"), value: o.supplierName },
            { label: t("attendance.date"), value: formatDate(o.orderDate, me.calendar, am) },
            { label: t("buy.requiredBy"), value: formatDate(o.requiredBy, me.calendar, am) },
            { label: t("buy.agr.agreement"), value: o.agreementId ? <Link to="/buying/agreements/$id" params={{ id: o.agreementId }}>{o.agreementNumber}</Link> : null, hidden: !o.agreementId },
            { label: t("acc.remark"), value: o.remark },
          ]} />
        ) },
        ...relatedSection(t, id, o.number),
        ...attachmentsSection(t, me, id),
        ...historySection(t, me, id, "PurchaseOrder")
      ]}
    />
  );
}
