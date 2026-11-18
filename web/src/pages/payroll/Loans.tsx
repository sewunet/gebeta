import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatDate, formatMoney, todayIso } from "../../lib/ethiopian";
import { useEmployees } from "../../lib/hooks";
import { can, type EmployeeLoanDto } from "../../lib/types";
import { DynamicPage, Fields, FilterBar, FilterField, ListReport, MessageStrip, ObjectPage, ObjectStatus, statusTone } from "../../ui/fiori";

type Kind = "Loan" | "StandingDeduction";
const loanTone = (status: string) => (status === "Running" ? "positive" : status === "Paused" ? "critical" : statusTone(status));

/** Loans and standing deductions with their balances: what each employee still owes, and what each salary slip will take. */
export function Loans() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const navigate = useNavigate();
  const [status, setStatus] = useState("open");
  const list = useQuery({
    queryKey: ["loans", company?.id],
    queryFn: () => api<EmployeeLoanDto[]>(`/api/payroll/loans?companyId=${company!.id}`),
    enabled: !!company,
  });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const rows = (list.data ?? []).filter((l) => status === "all" || (status === "open" ? ["Draft", "Running", "Paused"].includes(l.status) : l.status === status));
  const owed = rows.filter((l) => l.status !== "Cancelled").reduce((s, l) => s + (l.balance ?? 0), 0);
  const monthly = rows.filter((l) => l.status === "Running").reduce((s, l) => s + Math.min(l.instalment, l.balance ?? l.instalment), 0);

  return (
    <ListReport
      title={t("nav.loans")}
      filters={<FilterBar>
        <FilterField label={t("fields.status")}>
          <select value={status} onChange={(e) => setStatus(e.target.value)}>
            {["open", "Running", "Paused", "Completed", "Cancelled", "all"].map((s) => <option key={s} value={s}>{t(`loans.filter.${s}`)}</option>)}
          </select>
        </FilterField>
      </FilterBar>}
      tableTitle={`${t("nav.loans")} · ${t("loans.balance")} ${formatMoney(owed)} · ${t("loans.monthly")} ${formatMoney(monthly)}`}
      count={rows.length}
      toolbar={can(me, "payroll.loans.manage") && <button onClick={() => void navigate({ to: "/payroll/loans/new" })}>{t("ui.create")}</button>}
    >
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("fields.number")}</th><th>{t("fields.employee")}</th><th>{t("fields.component")}</th><th>{t("loans.start")}</th>
            <th className="num">{t("loans.instalment")}</th><th className="num">{t("loans.balance")}</th><th>{t("fields.status")}</th></tr></thead>
          <tbody>
            {rows.map((l) => (
              <tr key={l.id} className="navigable" onClick={() => void navigate({ to: "/payroll/loans/$id", params: { id: l.id } })}>
                <td data-label={t("fields.number")} className="mono"><span className="strong-link">{l.number}</span></td>
                <td data-label={t("fields.employee")}>{l.employeeName} <span className="muted mono small">{l.employeeNumber}</span></td>
                <td data-label={t("fields.component")}>{l.component} <span className="muted mono small">{l.abbreviation}</span></td>
                <td data-label={t("loans.start")}>{formatDate(l.startMonth, "Ethiopian", i18n.language === "am")}</td>
                <td data-label={t("loans.instalment")} className="num">{formatMoney(l.instalment)}</td>
                <td data-label={t("loans.balance")} className="num">{l.balance == null ? "–" : formatMoney(l.balance)}</td>
                <td data-label={t("fields.status")}><ObjectStatus tone={loanTone(l.status)}>{t(`loans.status.${l.status}`)}</ObjectStatus></td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <div className="empty">{list.isPending ? t("common.loading") : t("common.empty")}</div>}
    </ListReport>
  );
}

/** A new loan (amount and instalment) or standing deduction (instalment until stopped or an end month). */
export function LoanCreate() {
  const { t } = useTranslation();
  const { company } = useSession();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const employees = useEmployees();
  const [kind, setKind] = useState<Kind>("Loan");
  const [principal, setPrincipal] = useState("");
  const [instalment, setInstalment] = useState("");
  const create = useMutation({
    mutationFn: (b: Record<string, string>) => post<EmployeeLoanDto>("/api/payroll/loans", {
      companyId: company!.id, employeeId: b.employeeId, kind, component: b.component, abbreviation: kind === "Loan" ? null : b.abbreviation,
      principal: kind === "Loan" ? Number(principal) : null, instalment: Number(instalment), startMonth: b.startMonth,
      endMonth: kind === "Loan" || !b.endMonth ? null : b.endMonth, remark: b.remark || null,
    }),
    onSuccess: (l) => { void queryClient.invalidateQueries({ queryKey: ["loans"] }); void navigate({ to: "/payroll/loans/$id", params: { id: l.id } }); },
  });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const months = kind === "Loan" && Number(principal) > 0 && Number(instalment) > 0 ? Math.ceil(Number(principal) / Number(instalment)) : null;
  const onSubmit = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); create.mutate(Object.fromEntries(new FormData(e.currentTarget)) as Record<string, string>); };

  return (
    <DynamicPage
      title={t("loans.new")}
      breadcrumb={<Link to="/payroll/loans">{t("nav.loans")}</Link>}
      footer={<>
        {create.isError && <span className="error">{create.error.message}</span>}
        {months && <span className="muted">{t("loans.months", { count: months })}</span>}
        <button type="submit" form="loan-create" disabled={create.isPending}>{t("common.save")}</button>
        <button className="secondary" onClick={() => void navigate({ to: "/payroll/loans" })}>{t("common.cancel")}</button>
      </>}
    >
      <form id="loan-create" onSubmit={onSubmit}>
        <section className="object-section">
          <h2>{t("ui.general")}</h2>
          <div className="form-columns">
            <label>{t("fields.type")}
              <select value={kind} onChange={(e) => setKind(e.target.value as Kind)}>
                <option value="Loan">{t("loans.kind.Loan")}</option><option value="StandingDeduction">{t("loans.kind.StandingDeduction")}</option>
              </select>
            </label>
            <label>{t("fields.employee")}<span className="required" aria-hidden>*</span>
              <select name="employeeId" required defaultValue="">
                <option value="" disabled>—</option>
                {(employees.data ?? []).filter((e) => e.status !== "Left").map((e) => <option key={e.id} value={e.id}>{e.number} · {e.fullName}</option>)}
              </select>
            </label>
            <label>{t("fields.component")}<span className="required" aria-hidden>*</span>
              <input name="component" required placeholder={kind === "Loan" ? "Salary advance" : "Idir"} /></label>
            {kind === "StandingDeduction" && <label>{t("structures.abbreviation")}<span className="required" aria-hidden>*</span>
              <input name="abbreviation" required className="mono" placeholder="IDIR" maxLength={20} /></label>}
            {kind === "Loan" && <label>{t("loans.principal")}<span className="required" aria-hidden>*</span>
              <input type="number" min="0.01" step="0.01" required className="num" value={principal} onChange={(e) => setPrincipal(e.target.value)} /></label>}
            <label>{t("loans.instalment")}<span className="required" aria-hidden>*</span>
              <input type="number" min="0.01" step="0.01" required className="num" value={instalment} onChange={(e) => setInstalment(e.target.value)} /></label>
            <label>{t("loans.start")}<span className="required" aria-hidden>*</span><input type="date" name="startMonth" required defaultValue={todayIso()} /></label>
            {kind === "StandingDeduction" && <label>{t("loans.end")}<input type="date" name="endMonth" /></label>}
            <label>{t("acc.remark")}<input name="remark" /></label>
          </div>
          <p className="muted small">{t(kind === "Loan" ? "loans.loanHint" : "loans.standingHint")}</p>
        </section>
      </form>
    </DynamicPage>
  );
}

/** One loan or standing deduction: its balance and every instalment, with submit, pause, resume, settle or stop, and cancel. */
export function LoanDetail() {
  const { t, i18n } = useTranslation();
  const { me } = useSession();
  const queryClient = useQueryClient();
  const { id } = useParams({ from: "/payroll/loans/$id" });
  const [settling, setSettling] = useState(false);
  const loan = useQuery({ queryKey: ["loan", id], queryFn: () => api<EmployeeLoanDto>(`/api/payroll/loans/${id}`) });
  const done = (l: EmployeeLoanDto) => { queryClient.setQueryData(["loan", id], l); void queryClient.invalidateQueries({ queryKey: ["loans"] }); setSettling(false); };
  const act = useMutation({ mutationFn: (action: "submit" | "pause" | "resume" | "cancel") => post<EmployeeLoanDto>(`/api/payroll/loans/${id}/${action}`), onSuccess: done });
  const settle = useMutation({
    mutationFn: (b: Record<string, string>) => post<EmployeeLoanDto>(`/api/payroll/loans/${id}/settle`, { date: b.date, reference: b.reference || null }),
    onSuccess: done,
  });
  if (loan.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (loan.isError) return <MessageStrip tone="negative">{loan.error.message}</MessageStrip>;
  const l = loan.data;
  const am = i18n.language === "am";
  const isLoan = l.kind === "Loan";
  const manage = can(me, "payroll.loans.manage");
  const running = l.status === "Running" || l.status === "Paused";
  const onSettle = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); settle.mutate(Object.fromEntries(new FormData(e.currentTarget)) as Record<string, string>); };

  return (
    <ObjectPage
      breadcrumb={<Link to="/payroll/loans">{t("nav.loans")}</Link>}
      title={`${l.component} · ${l.employeeName}`}
      subtitle={<><span className="mono">{l.number}</span> · {t(`loans.kind.${l.kind}`)} · <span className="mono">{l.employeeNumber}</span></>}
      status={<ObjectStatus tone={loanTone(l.status)}>{t(`loans.status.${l.status}`)}</ObjectStatus>}
      kpis={[
        ...(isLoan ? [{ label: t("loans.principal"), value: formatMoney(l.principal ?? 0) }] : []),
        { label: t("loans.instalment"), value: formatMoney(l.instalment) },
        { label: t("loans.recovered"), value: formatMoney(l.recovered) },
        ...(isLoan ? [{ label: t("loans.balance"), value: formatMoney(l.balance ?? 0), tone: (l.balance ?? 0) > 0 ? "critical" as const : "positive" as const }] : []),
      ]}
      actions={manage && <>
        {l.status === "Draft" && <button onClick={() => act.mutate("submit")} disabled={act.isPending}>{t("slips.submit")}</button>}
        {l.status === "Running" && <button className="secondary" onClick={() => act.mutate("pause")} disabled={act.isPending}>{t("loans.pause")}</button>}
        {l.status === "Paused" && <button onClick={() => act.mutate("resume")} disabled={act.isPending}>{t("loans.resume")}</button>}
        {running && <button className="secondary" onClick={() => setSettling(true)}>{t(isLoan ? "loans.settle" : "loans.stop")}</button>}
        {(l.status === "Draft" || running) && <button className="secondary" onClick={() => act.mutate("cancel")} disabled={act.isPending}>{t("loans.cancel")}</button>}
      </>}
      messages={<>
        {l.status === "Running" && <MessageStrip tone="information">{t("loans.runningHint", { start: formatDate(l.startMonth, "Ethiopian", am) })}</MessageStrip>}
        {(act.isError || settle.isError) && <MessageStrip tone="negative">{(act.error ?? settle.error)?.message}</MessageStrip>}
        {settling && (
          <form className="inline-form" onSubmit={onSettle}>
            <span>{t(isLoan ? "loans.settleHint" : "loans.stopHint", { balance: formatMoney(l.balance ?? 0) })}</span>
            <input type="date" name="date" required defaultValue={todayIso()} aria-label={t("attendance.date")} />
            <input name="reference" placeholder={t("loans.reference")} aria-label={t("loans.reference")} />
            <button type="submit" disabled={settle.isPending}>{t(isLoan ? "loans.settle" : "loans.stop")}</button>
            <button type="button" className="secondary" onClick={() => setSettling(false)}>{t("common.cancel")}</button>
          </form>
        )}
      </>}
      sections={[
        { id: "terms", title: t("ui.general"), content: (
          <Fields items={[
            { label: t("loans.start"), value: formatDate(l.startMonth, "Both", am) },
            { label: t("loans.end"), value: l.endMonth ? formatDate(l.endMonth, "Both", am) : "", hidden: !l.endMonth },
            { label: t("structures.abbreviation"), value: <span className="mono">{l.abbreviation}</span> },
            { label: t("acc.remark"), value: l.remark, hidden: !l.remark },
          ]} />
        ) },
        { id: "repayments", title: t("loans.repayments"), content: l.repayments.length ? (
          <table className="table responsive">
            <thead><tr><th>{t("attendance.date")}</th><th>{t("loans.source")}</th><th>{t("loans.reference")}</th><th className="num">{t("fields.amount")}</th></tr></thead>
            <tbody>{l.repayments.map((r, i) => (
              <tr key={i}>
                <td>{formatDate(r.date, "Ethiopian", am)}</td>
                <td>{r.source === "Payroll" ? (r.inSlip ? t("loans.fromSlip") : t("loans.waitingSlip")) : t("loans.manual")}</td>
                <td>{r.reference}</td><td className="num">{formatMoney(r.amount)}</td>
              </tr>
            ))}</tbody>
            <tfoot><tr><td colSpan={3}>{t("loans.recovered")}</td><td className="num">{formatMoney(l.recovered)}</td></tr></tfoot>
          </table>
        ) : <div className="empty">{t("loans.noRepayments")}</div> },
      ]}
    />
  );
}
