import { useMemo, useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatDate, formatMoney, todayIso } from "../../lib/ethiopian";
import { can, type BlanketAgreementDto } from "../../lib/types";
import { DynamicPage, Fields, FilterBar, FilterField, ListReport, MessageStrip, ObjectPage, ObjectStatus, type Tone } from "../../ui/fiori";
import { useBlanketAgreement, useItems, useParties } from "../../lib/queries";
import { historySection } from "../../app/history";
import { attachmentsSection } from "../../app/attachments";
import { relatedSection } from "../../app/related";

const TONE: Record<BlanketAgreementDto["state"], Tone> = { Draft: "critical", Active: "positive", Exhausted: "information", Expired: "neutral", Cancelled: "negative" };

/** List report of framework agreements with what has been called off. */
export function Agreements() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const navigate = useNavigate();
  const [state, setState] = useState("");
  const [search, setSearch] = useState("");
  const list = useQuery({
    queryKey: ["agreements", company?.id],
    queryFn: () => api<BlanketAgreementDto[]>(`/api/buying/agreements?companyId=${company!.id}`),
    enabled: !!company,
  });
  const rows = useMemo(() => (list.data ?? []).filter((a) => (!state || a.state === state)
    && (!search.trim() || [a.number, a.supplierName].some((v) => v?.toLowerCase().includes(search.trim().toLowerCase())))), [list.data, state, search]);
  if (!company) return <p>{t("common.noCompany")}</p>;
  const am = i18n.language === "am";

  return (
    <ListReport
      title={t("nav.agreements")}
      filters={
        <FilterBar>
          <FilterField label={t("ui.search")}><input type="search" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t("buy.agr.searchHint")} /></FilterField>
          <FilterField label={t("fields.status")}>
            <select value={state} onChange={(e) => setState(e.target.value)}>
              <option value="">{t("ui.all")}</option>
              {(Object.keys(TONE) as BlanketAgreementDto["state"][]).map((s) => <option key={s} value={s}>{t(`buy.agr.states.${s}`)}</option>)}
            </select>
          </FilterField>
        </FilterBar>
      }
      tableTitle={t("nav.agreements")}
      count={rows.length}
      toolbar={can(me, "buying.agreements.create") && <button onClick={() => void navigate({ to: "/buying/agreements/new" })}>{t("ui.create")}</button>}
    >
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("fields.number")}</th><th>{t("inv.supplier")}</th><th>{t("buy.agr.validFrom")}</th><th>{t("buy.agr.validTo")}</th>
            <th className="num">{t("buy.agr.called")}</th><th className="num">{t("buy.agr.remaining")}</th><th>{t("fields.status")}</th></tr></thead>
          <tbody>{rows.map((a) => (
            <tr key={a.id} className="navigable" onClick={() => void navigate({ to: "/buying/agreements/$id", params: { id: a.id } })}>
              <td data-label={t("fields.number")}><span className="mono strong-link">{a.number}</span></td>
              <td data-label={t("inv.supplier")}>{a.supplierName}</td>
              <td data-label={t("buy.agr.validFrom")}>{formatDate(a.validFrom, me.calendar, am)}</td>
              <td data-label={t("buy.agr.validTo")}>{formatDate(a.validTo, me.calendar, am)}</td>
              <td data-label={t("buy.agr.called")} className="num">{formatMoney(a.calledAmount)}</td>
              <td data-label={t("buy.agr.remaining")} className="num">{a.remainingAmount == null ? "" : formatMoney(a.remainingAmount)}</td>
              <td data-label={t("fields.status")}><ObjectStatus tone={TONE[a.state]}>{t(`buy.agr.states.${a.state}`)}</ObjectStatus></td>
            </tr>
          ))}</tbody>
        </table>
      ) : <div className="empty">{list.isPending ? t("common.loading") : t("common.empty")}</div>}
    </ListReport>
  );
}

type Line = { itemId: string; rate: string; plannedQty: string };

/** A new framework agreement. */
export function AgreementCreate() {
  const { t } = useTranslation();
  const { company } = useSession();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const parties = useParties();
  const items = useItems();
  const [lines, setLines] = useState<Line[]>([{ itemId: "", rate: "", plannedQty: "" }]);
  const create = useMutation({
    mutationFn: (body: object) => post<BlanketAgreementDto>("/api/buying/agreements", body),
    onSuccess: (a) => { void queryClient.invalidateQueries({ queryKey: ["agreements"] }); void navigate({ to: "/buying/agreements/$id", params: { id: a.id } }); },
  });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const setLine = (i: number, patch: Partial<Line>) => setLines((ls) => ls.map((l, j) => (j === i ? { ...l, ...patch } : l)));
  const onSubmit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    create.mutate({
      companyId: company.id, supplierId: f.get("supplierId"), agreementDate: f.get("agreementDate"), validFrom: f.get("validFrom"), validTo: f.get("validTo"),
      amountLimit: f.get("amountLimit") ? Number(f.get("amountLimit")) : null, remark: f.get("remark") || null,
      lines: lines.filter((l) => l.itemId).map((l) => ({ itemId: l.itemId, rate: Number(l.rate || 0), plannedQty: l.plannedQty ? Number(l.plannedQty) : null })),
    });
  };

  return (
    <DynamicPage
      title={t("buy.agr.new")}
      breadcrumb={<Link to="/buying/agreements">{t("nav.agreements")}</Link>}
      footer={<>
        {create.isError && <span className="error">{create.error.message}</span>}
        <button type="submit" form="agr-create" disabled={create.isPending}>{t("inv.saveDraft")}</button>
        <button className="secondary" onClick={() => void navigate({ to: "/buying/agreements" })}>{t("common.cancel")}</button>
      </>}
    >
      <form id="agr-create" onSubmit={onSubmit}>
        <section className="object-section">
          <h2>{t("ui.general")}</h2>
          <div className="form-columns">
            <label>{t("inv.supplier")}<span className="required" aria-hidden>*</span>
              <select name="supplierId" required defaultValue="">
                <option value="" disabled>—</option>
                {parties.data?.filter((p) => p.type !== "Customer").map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
              </select>
            </label>
            <label>{t("attendance.date")}<span className="required" aria-hidden>*</span><input type="date" name="agreementDate" defaultValue={todayIso()} required /></label>
            <label>{t("buy.agr.validFrom")}<span className="required" aria-hidden>*</span><input type="date" name="validFrom" defaultValue={todayIso()} required /></label>
            <label>{t("buy.agr.validTo")}<span className="required" aria-hidden>*</span><input type="date" name="validTo" required /></label>
            <label>{t("buy.agr.amountLimit")}<input type="number" name="amountLimit" step="0.01" min="0" /></label>
            <label>{t("acc.remark")}<input name="remark" /></label>
          </div>
          <p className="muted">{t("buy.agr.capHint")}</p>
        </section>
        <section className="object-section">
          <h2>{t("ui.lines")}</h2>
          <table className="table edit-table">
            <thead><tr><th>{t("nav.items")}</th><th className="num">{t("buy.agr.agreedRate")}</th><th className="num">{t("buy.agr.planned")}</th><th /></tr></thead>
            <tbody>{lines.map((l, i) => (
              <tr key={i}>
                <td><select aria-label={t("nav.items")} value={l.itemId} onChange={(e) => setLine(i, { itemId: e.target.value })} required={i === 0}>
                  <option value="">—</option>{items.data?.filter((x) => x.isActive).map((x) => <option key={x.id} value={x.id}>{x.code} · {x.name} ({x.uom})</option>)}
                </select></td>
                <td><input aria-label={t("buy.agr.agreedRate")} className="num" type="number" step="0.01" min="0" value={l.rate} onChange={(e) => setLine(i, { rate: e.target.value })} /></td>
                <td><input aria-label={t("buy.agr.planned")} className="num" type="number" step="0.001" min="0" value={l.plannedQty} onChange={(e) => setLine(i, { plannedQty: e.target.value })} /></td>
                <td>{lines.length > 1 && <button type="button" className="link-button" aria-label={t("ui.remove")} onClick={() => setLines((ls) => ls.filter((_, j) => j !== i))}>×</button>}</td>
              </tr>
            ))}</tbody>
            <tfoot><tr><td colSpan={4}><button type="button" className="secondary" onClick={() => setLines((ls) => [...ls, { itemId: "", rate: "", plannedQty: "" }])}>{t("acc.addLine")}</button></td></tr></tfoot>
          </table>
        </section>
      </form>
    </DynamicPage>
  );
}

/** Object page of an agreement: what is agreed, called off and left per item, and the orders that took it. */
export function AgreementDetail() {
  const { t, i18n } = useTranslation();
  const { me } = useSession();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { id } = useParams({ from: "/buying/agreements/$id" });
  const [notice, setNotice] = useState<string | null>(null);
  const agreement = useBlanketAgreement(id);
  const act = useMutation({
    mutationFn: (action: "submit" | "cancel") => post<BlanketAgreementDto>(`/api/buying/agreements/${id}/${action}`),
    onSuccess: (a) => { queryClient.setQueryData(["agreement", id], a); void queryClient.invalidateQueries({ queryKey: ["agreements"] }); setNotice(t(`buy.agr.states.${a.state}`)); },
  });
  if (agreement.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (agreement.isError) return <MessageStrip tone="negative">{agreement.error.message}</MessageStrip>;
  const a = agreement.data;
  const am = i18n.language === "am";

  return (
    <ObjectPage
      breadcrumb={<Link to="/buying/agreements">{t("nav.agreements")}</Link>}
      title={a.number ?? t("status.Draft")}
      subtitle={a.supplierName}
      status={<ObjectStatus tone={TONE[a.state]}>{t(`buy.agr.states.${a.state}`)}</ObjectStatus>}
      kpis={[
        ...(a.amountLimit != null ? [{ label: t("buy.agr.amountLimit"), value: formatMoney(a.amountLimit) }] : []),
        { label: t("buy.agr.called"), value: formatMoney(a.calledAmount) },
        ...(a.remainingAmount != null ? [{ label: t("buy.agr.remaining"), value: formatMoney(a.remainingAmount), tone: a.remainingAmount === 0 ? "critical" as const : undefined }] : []),
        { label: t("buy.agr.validTo"), value: formatDate(a.validTo, me.calendar, am), tone: a.state === "Expired" ? "negative" as const : undefined },
      ]}
      actions={<>
        {a.state === "Active" && can(me, "buying.orders.create") &&
          <button onClick={() => void navigate({ to: "/buying/orders/new", search: { agreement: a.id } })}>{t("buy.agr.callOff")}</button>}
        {a.status === "Draft" && can(me, "buying.agreements.submit") && <button onClick={() => act.mutate("submit")} disabled={act.isPending}>{t("slips.submit")}</button>}
        {a.status === "Submitted" && a.callOffs.length === 0 && can(me, "buying.agreements.submit") &&
          <button className="secondary" onClick={() => act.mutate("cancel")} disabled={act.isPending}>{t("common.cancel")}</button>}
      </>}
      messages={<>
        {notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
        {act.isError && <MessageStrip tone="negative">{act.error.message}</MessageStrip>}
        {a.status === "Draft" && <MessageStrip tone="critical">{t("buy.agr.draftHint")}</MessageStrip>}
      </>}
      sections={[
        { id: "lines", title: t("ui.lines"), content: (
          <table className="table">
            <thead><tr><th>{t("nav.items")}</th><th className="num">{t("buy.agr.agreedRate")}</th><th className="num">{t("buy.agr.planned")}</th>
              <th className="num">{t("buy.agr.calledQty")}</th><th className="num">{t("cash.amount")}</th><th className="num">{t("buy.agr.remaining")}</th></tr></thead>
            <tbody>{a.lines.map((l) => (
              <tr key={l.itemId}>
                <td><span className="mono">{l.itemCode}</span> · {l.itemName}</td><td className="num">{formatMoney(l.rate)}</td>
                <td className="num">{l.plannedQty == null ? "—" : `${l.plannedQty} ${l.uom}`}</td>
                <td className="num">{l.calledQty}</td><td className="num">{formatMoney(l.calledAmount)}</td>
                <td className="num">{l.remainingQty == null ? "—" : <ObjectStatus tone={l.remainingQty === 0 ? "critical" : "positive"}>{l.remainingQty}</ObjectStatus>}</td>
              </tr>
            ))}</tbody>
          </table>
        ) },
        { id: "calloffs", title: t("buy.agr.callOffs"), content: a.callOffs.length ? (
          <table className="table">
            <thead><tr><th>{t("fields.number")}</th><th>{t("attendance.date")}</th><th className="num">{t("cash.amount")}</th></tr></thead>
            <tbody>{a.callOffs.map((o) => (
              <tr key={o.orderId}>
                <td><Link to="/buying/orders/$id" params={{ id: o.orderId }} className="mono">{o.number}</Link></td>
                <td>{formatDate(o.orderDate, me.calendar, am)}</td><td className="num">{formatMoney(o.amount)}</td>
              </tr>
            ))}</tbody>
          </table>
        ) : <div className="empty">{t("common.empty")}</div> },
        { id: "general", title: t("ui.general"), content: (
          <Fields items={[
            { label: t("inv.supplier"), value: a.supplierName },
            { label: t("attendance.date"), value: formatDate(a.agreementDate, me.calendar, am) },
            { label: t("buy.agr.validFrom"), value: formatDate(a.validFrom, me.calendar, am) },
            { label: t("buy.agr.validTo"), value: formatDate(a.validTo, me.calendar, am) },
            { label: t("acc.remark"), value: a.remark },
          ]} />
        ) },
        ...relatedSection(t, id, a.number),
        ...attachmentsSection(t, me, id),
        ...historySection(t, me, id, "BlanketAgreement"),
      ]}
    />
  );
}
