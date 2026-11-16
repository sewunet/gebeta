import { useEffect, useMemo, useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams, useSearch } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatDate, formatMoney, todayIso } from "../../lib/ethiopian";
import { can, type StockEntryDto } from "../../lib/types";
import { DynamicPage, Fields, FilterBar, FilterField, ListReport, MessageStrip, ObjectPage, ObjectStatus, statusTone } from "../../ui/fiori";
import { currentMonth, monthRange } from "../payroll/month";
import { useItems, useWarehouses } from "./hooks";
import { useParties } from "../accounting/Parties";
import { usePurchaseOrder } from "../buying/PurchaseOrders";

const refreshKeys = ["stock-entries", "stock-entry", "stock-balance", "stock-ledger", "item-setting"];

/** List report of stock entries. */
export function StockEntries() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const navigate = useNavigate();
  const month = monthRange(currentMonth());
  const [from, setFrom] = useState(month.start);
  const [to, setTo] = useState(month.end);
  const [purpose, setPurpose] = useState("");
  const [status, setStatus] = useState("");
  const [search, setSearch] = useState("");
  const entries = useQuery({
    queryKey: ["stock-entries", company?.id, from, to],
    queryFn: () => api<StockEntryDto[]>(`/api/stock/entries?companyId=${company!.id}&from=${from}&to=${to}`),
    enabled: !!company && from <= to,
  });
  const rows = useMemo(() => (entries.data ?? []).filter((e) => (!purpose || e.purpose === purpose) && (!status || e.status === status)
    && (!search.trim() || [e.number, e.remark, ...e.lines.map((l) => `${l.itemCode} ${l.itemName}`)].some((v) => v?.toLowerCase().includes(search.trim().toLowerCase())))),
  [entries.data, purpose, status, search]);
  if (!company) return <p>{t("common.noCompany")}</p>;
  const am = i18n.language === "am";

  return (
    <ListReport
      title={t("nav.stockEntries")}
      filters={
        <FilterBar>
          <FilterField label={t("ui.search")}><input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t("stock.entrySearchHint")} /></FilterField>
          <FilterField label={t("stock.purpose")}>
            <select value={purpose} onChange={(e) => setPurpose(e.target.value)}>
              <option value="">{t("ui.all")}</option>{(["Receipt", "Issue", "Transfer"] as const).map((p) => <option key={p} value={p}>{t(`stock.purposes.${p}`)}</option>)}
            </select>
          </FilterField>
          <FilterField label={t("fields.status")}>
            <select value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="">{t("ui.all")}</option>{["Draft", "Submitted", "Cancelled"].map((s) => <option key={s} value={s}>{t(`status.${s}`)}</option>)}
            </select>
          </FilterField>
          <FilterField label={t("periods.from")}><input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></FilterField>
          <FilterField label={t("periods.to")}><input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></FilterField>
        </FilterBar>
      }
      tableTitle={t("nav.stockEntries")}
      count={rows.length}
      toolbar={can(me, "stock.entries.create") && (["Receipt", "Issue", "Transfer"] as const).map((p, i) => (
        <button key={p} className={i ? "secondary" : ""} onClick={() => void navigate({ to: "/stock/entries/new", search: { purpose: p } })}>{t(`stock.new.${p}`)}</button>
      ))}
    >
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("fields.number")}</th><th>{t("attendance.date")}</th><th>{t("stock.purpose")}</th><th>{t("nav.items")}</th><th className="num">{t("stock.value")}</th><th>{t("fields.status")}</th></tr></thead>
          <tbody>{rows.map((e) => (
            <tr key={e.id} className="navigable" onClick={() => void navigate({ to: "/stock/entries/$id", params: { id: e.id } })}>
              <td data-label={t("fields.number")}><span className="mono strong-link">{e.number}</span></td>
              <td data-label={t("attendance.date")}>{formatDate(e.postingDate, me.calendar, am)}</td>
              <td data-label={t("stock.purpose")}>{t(`stock.purposes.${e.purpose}`)}{e.partyName && <div className="muted small">{e.partyName}</div>}</td>
              <td data-label={t("nav.items")}>{e.lines.map((l) => `${l.itemCode} × ${l.qty}`).join(", ")}{e.remark && <div className="muted small">{e.remark}</div>}</td>
              <td data-label={t("stock.value")} className="num">{e.status === "Draft" ? "" : formatMoney(e.totalValue)}</td>
              <td data-label={t("fields.status")}><ObjectStatus tone={statusTone(e.status)}>{t(`status.${e.status}`)}</ObjectStatus></td>
            </tr>
          ))}</tbody>
        </table>
      ) : <div className="empty">{entries.isPending ? t("common.loading") : t("common.empty")}</div>}
    </ListReport>
  );
}

type Line = { itemId: string; qty: string; rate: string; source: string; target: string };

/** A new stock entry; Save posts it straight away (Save as draft keeps it for later). */
export function StockEntryCreate() {
  const { t } = useTranslation();
  const { company } = useSession();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { purpose, po } = useSearch({ from: "/stock/entries/new" });
  const order = usePurchaseOrder(po);
  const items = useItems();
  const warehouses = useWarehouses();
  const parties = useParties();
  const [partyId, setPartyId] = useState("");
  const first = warehouses.data?.[0]?.id ?? "";
  const second = warehouses.data?.[1]?.id ?? "";
  const empty = (): Line => ({ itemId: "", qty: "", rate: "", source: purpose === "Receipt" ? "" : first, target: purpose === "Issue" ? "" : purpose === "Transfer" ? second : first });
  const [lines, setLines] = useState<Line[]>([]);
  const shown = lines.length ? lines : [empty()];
  useEffect(() => {
    // Receiving against a purchase order: its supplier, its number as the reference, and what is still to come.
    if (!order.data || purpose !== "Receipt") return;
    setPartyId(order.data.supplierId);
    setLines(order.data.lines.filter((l) => l.qty > l.receivedQty).map((l) => ({
      itemId: l.itemId, qty: String(l.qty - l.receivedQty), rate: String(l.rate), source: "", target: l.warehouseId ?? first,
    })));
  }, [order.data, purpose, first]);
  const create = useMutation({
    mutationFn: async ({ body, submit }: { body: object; submit: boolean }) => {
      const draft = await post<StockEntryDto>("/api/stock/entries", body);
      return submit ? post<StockEntryDto>(`/api/stock/entries/${draft.id}/submit`) : draft;
    },
    onSuccess: (e) => { for (const k of refreshKeys) void queryClient.invalidateQueries({ queryKey: [k] }); void navigate({ to: "/stock/entries/$id", params: { id: e.id } }); },
  });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const stockItems = items.data?.filter((i) => i.isStockItem && i.isActive) ?? [];
  const setLine = (i: number, patch: Partial<Line>) => setLines(shown.map((l, j) => (j === i ? { ...l, ...patch } : l)));
  const total = shown.reduce((a, l) => a + Number(l.qty || 0) * Number(l.rate || 0), 0);
  const save = (submit: boolean) => (e?: FormEvent<HTMLFormElement>) => {
    e?.preventDefault();
    const form = document.getElementById("stock-entry") as HTMLFormElement;
    if (!form.reportValidity()) return;
    const f = new FormData(form);
    create.mutate({
      submit,
      body: {
        companyId: company.id, purpose, postingDate: f.get("postingDate"), remark: f.get("remark") || null, counterAccountCode: f.get("counterAccountCode") || null,
        partyId: partyId || null, reference: f.get("reference") || null,
        lines: shown.filter((l) => l.itemId && Number(l.qty) > 0).map((l) => ({
          itemId: l.itemId, qty: Number(l.qty), rate: purpose === "Receipt" ? Number(l.rate || 0) : null,
          sourceWarehouseId: purpose === "Receipt" ? null : l.source, targetWarehouseId: purpose === "Issue" ? null : l.target,
        })),
      },
    });
  };
  const whSelect = (value: string, onChange: (v: string) => void, label: string) => (
    <select aria-label={label} value={value} onChange={(e) => onChange(e.target.value)} required>
      <option value="">—</option>{warehouses.data?.map((w) => <option key={w.id} value={w.id}>{w.code} · {w.name}</option>)}
    </select>
  );

  return (
    <DynamicPage
      title={t(`stock.new.${purpose}`)}
      breadcrumb={<Link to="/stock/entries">{t("nav.stockEntries")}</Link>}
      footer={<>
        {create.isError && <span className="error">{create.error.message}</span>}
        <button onClick={() => save(true)()} disabled={create.isPending}>{t("stock.saveAndPost")}</button>
        <button className="secondary" onClick={() => save(false)()} disabled={create.isPending}>{t("inv.saveDraft")}</button>
        <button className="secondary" onClick={() => void navigate({ to: "/stock/entries" })}>{t("common.cancel")}</button>
      </>}
    >
      {!warehouses.data?.length && !warehouses.isPending && <MessageStrip tone="critical">{t("stock.noWarehouses")}</MessageStrip>}
      {order.data && <MessageStrip tone="information">{t("buy.receivingAgainst", { number: order.data.number, supplier: order.data.supplierName })}</MessageStrip>}
      <form id="stock-entry" onSubmit={save(true)}>
        <section className="object-section">
          <h2>{t("ui.general")}</h2>
          <div className="form-columns">
            <label>{t("attendance.date")}<span className="required" aria-hidden>*</span><input type="date" name="postingDate" defaultValue={todayIso()} max={todayIso()} required /></label>
            {purpose !== "Transfer" && <>
              <label>{t(purpose === "Receipt" ? "inv.supplier" : "inv.customer")}
                <select value={partyId} onChange={(e) => setPartyId(e.target.value)}>
                  <option value="">{t("stock.noParty")}</option>
                  {parties.data?.filter((p) => p.type === "Both" || p.type === (purpose === "Receipt" ? "Supplier" : "Customer")).map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                </select>
              </label>
              <label>{t("inv.reference")}<input name="reference" key={order.data?.number ?? "none"} defaultValue={order.data?.number ?? ""} placeholder={purpose === "Receipt" ? "DN / GRN" : "SINV"} /></label>
              <label>{t("stock.counterAccount")}<input name="counterAccountCode" key={partyId ? "p" : "n"} placeholder={partyId ? (purpose === "Receipt" ? "2150" : "5000") : "5100"} /></label>
            </>}
            <label>{t("acc.remark")}<input name="remark" /></label>
          </div>
        </section>
        <section className="object-section">
          <h2>{t("ui.lines")}</h2>
          <table className="table edit-table">
            <thead><tr><th>{t("nav.items")}</th><th className="num">{t("stock.qty")}</th>
              {purpose === "Receipt" && <th className="num">{t("inv.rate")}</th>}
              {purpose !== "Receipt" && <th>{t("stock.from")}</th>}{purpose !== "Issue" && <th>{t("stock.to")}</th>}<th /></tr></thead>
            <tbody>{shown.map((l, i) => (
              <tr key={i}>
                <td><select aria-label={t("nav.items")} value={l.itemId} onChange={(e) => setLine(i, { itemId: e.target.value })} required={i === 0}>
                  <option value="">—</option>{stockItems.map((it) => <option key={it.id} value={it.id}>{it.code} · {it.name} ({it.uom})</option>)}
                </select></td>
                <td><input aria-label={t("stock.qty")} className="num" type="number" step="0.001" min="0" value={l.qty} onChange={(e) => setLine(i, { qty: e.target.value })} /></td>
                {purpose === "Receipt" && <td><input aria-label={t("inv.rate")} className="num" type="number" step="0.01" min="0" value={l.rate} onChange={(e) => setLine(i, { rate: e.target.value })} /></td>}
                {purpose !== "Receipt" && <td>{whSelect(l.source, (v) => setLine(i, { source: v }), t("stock.from"))}</td>}
                {purpose !== "Issue" && <td>{whSelect(l.target, (v) => setLine(i, { target: v }), t("stock.to"))}</td>}
                <td>{shown.length > 1 && <button type="button" className="link-button" aria-label={t("ui.remove")} onClick={() => setLines(shown.filter((_, j) => j !== i))}>×</button>}</td>
              </tr>
            ))}</tbody>
            <tfoot><tr><td colSpan={purpose === "Transfer" ? 4 : 3}><button type="button" className="secondary" onClick={() => setLines([...shown, empty()])}>{t("acc.addLine")}</button></td>
              {purpose === "Receipt" ? <td className="num">{formatMoney(total)}</td> : <td />}</tr></tfoot>
          </table>
          <MessageStrip tone="information">{t(purpose === "Receipt" ? "stock.receiptHint" : "stock.rateHint")}
            {partyId && <> {t(purpose === "Receipt" ? "stock.receivedHint" : "stock.deliveredHint")}</>}</MessageStrip>
        </section>
      </form>
    </DynamicPage>
  );
}

/** Object page of one stock entry. */
export function StockEntryDetail() {
  const { t, i18n } = useTranslation();
  const { me } = useSession();
  const queryClient = useQueryClient();
  const { id } = useParams({ from: "/stock/entries/$id" });
  const [notice, setNotice] = useState<string | null>(null);
  const entry = useQuery({ queryKey: ["stock-entry", id], queryFn: () => api<StockEntryDto>(`/api/stock/entries/${id}`) });
  const act = useMutation({
    mutationFn: (action: "submit" | "cancel") => post<StockEntryDto>(`/api/stock/entries/${id}/${action}`),
    onSuccess: (e) => {
      queryClient.setQueryData(["stock-entry", id], e);
      for (const k of refreshKeys) void queryClient.invalidateQueries({ queryKey: [k] });
      setNotice(t(e.status === "Cancelled" ? "stock.cancelled" : "stock.posted", { number: e.number }));
    },
  });
  if (entry.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (entry.isError) return <MessageStrip tone="negative">{entry.error.message}</MessageStrip>;
  const e = entry.data;
  const am = i18n.language === "am";

  return (
    <ObjectPage
      breadcrumb={<Link to="/stock/entries">{t("nav.stockEntries")}</Link>}
      title={e.number ?? t("status.Draft")}
      subtitle={<>{t(`stock.purposes.${e.purpose}`)}{e.partyName && <> · {e.partyName}</>}{e.remark && <> · {e.remark}</>}</>}
      status={<ObjectStatus tone={statusTone(e.status)}>{t(`status.${e.status}`)}</ObjectStatus>}
      kpis={[
        { label: t("stock.value"), value: e.status === "Draft" ? "—" : formatMoney(e.totalValue) },
        { label: t("attendance.date"), value: formatDate(e.postingDate, me.calendar, am) },
        { label: t("ui.lines"), value: String(e.lines.length) },
      ]}
      actions={<>
        {e.status === "Draft" && can(me, "stock.entries.submit") && <button onClick={() => act.mutate("submit")} disabled={act.isPending}>{t("acc.post")}</button>}
        {e.status === "Submitted" && can(me, "stock.entries.submit") && <button className="secondary" onClick={() => act.mutate("cancel")} disabled={act.isPending}>{t("common.cancel")}</button>}
      </>}
      messages={<>
        {notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
        {act.isError && <MessageStrip tone="negative">{act.error.message}</MessageStrip>}
        {e.status === "Draft" && <MessageStrip tone="critical">{t("stock.draftHint")}</MessageStrip>}
      </>}
      sections={[
        { id: "lines", title: t("ui.lines"), content: (
          <table className="table">
            <thead><tr><th>{t("nav.items")}</th><th className="num">{t("stock.qty")}</th>{e.purpose !== "Receipt" && <th>{t("stock.from")}</th>}
              {e.purpose !== "Issue" && <th>{t("stock.to")}</th>}<th className="num">{t("inv.rate")}</th><th className="num">{t("stock.value")}</th></tr></thead>
            <tbody>{e.lines.map((l, i) => (
              <tr key={i}>
                <td><span className="mono">{l.itemCode}</span> · {l.itemName}</td><td className="num">{l.qty} {l.uom}</td>
                {e.purpose !== "Receipt" && <td className="mono">{l.sourceWarehouse}</td>}{e.purpose !== "Issue" && <td className="mono">{l.targetWarehouse}</td>}
                <td className="num">{l.rate != null ? formatMoney(l.rate) : l.value ? formatMoney(l.value / l.qty) : "—"}</td>
                <td className="num">{e.status === "Draft" ? "" : formatMoney(l.value)}</td>
              </tr>
            ))}</tbody>
            {e.status !== "Draft" && <tfoot><tr><td colSpan={e.purpose === "Transfer" ? 5 : 4}>{t("common.total")}</td><td className="num">{formatMoney(e.totalValue)}</td></tr></tfoot>}
          </table>
        ) },
        { id: "general", title: t("ui.general"), content: (
          <Fields items={[
            { label: t("stock.purpose"), value: t(`stock.purposes.${e.purpose}`) },
            { label: t("attendance.date"), value: formatDate(e.postingDate, me.calendar, am) },
            { label: t(e.purpose === "Receipt" ? "inv.supplier" : "inv.customer"), value: e.partyName, hidden: !e.partyName },
            { label: t("inv.reference"), value: e.reference, hidden: !e.reference },
            { label: t("stock.counterAccount"), value: <span className="mono">{e.counterAccountCode}</span>, hidden: e.purpose === "Transfer" },
            { label: t("acc.remark"), value: e.remark },
          ]} />
        ) },
      ]}
    />
  );
}
