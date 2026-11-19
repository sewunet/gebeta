import { useMemo, useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatMoney, todayIso } from "../../lib/ethiopian";
import { useEmployees } from "../../lib/hooks";
import { can, type AdditionalSalaryDto } from "../../lib/types";
import { DynamicPage, FilterBar, FilterField, ListReport, MessageStrip, ObjectPage, ObjectStatus, statusTone } from "../../ui/fiori";
import { parsePastedRows } from "./pasteRows";
import { historySection } from "../../app/history";
import { attachmentsSection } from "../../app/attachments";
import { relatedSection } from "../../app/related";

const monthStart = (iso: string) => `${iso.slice(0, 7)}-01`;

/** Additional salaries of a period: overtime, allowances and deductions entered for many employees at once. */
export function AdditionalSalaries() {
  const { t } = useTranslation();
  const { me, company } = useSession();
  const navigate = useNavigate();
  const today = todayIso();
  const [from, setFrom] = useState(`${today.slice(0, 4)}-01-01`);
  const [to, setTo] = useState(`${today.slice(0, 4)}-12-31`);
  const list = useQuery({
    queryKey: ["additional-salaries", company?.id, from, to],
    queryFn: () => api<AdditionalSalaryDto[]>(`/api/payroll/additional-salaries?companyId=${company!.id}&from=${from}&to=${to}`),
    enabled: !!company,
  });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const rows = list.data ?? [];

  return (
    <ListReport
      title={t("nav.additionalSalary")}
      filters={<FilterBar>
        <FilterField label={t("leave.from")}><input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></FilterField>
        <FilterField label={t("leave.to")}><input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></FilterField>
      </FilterBar>}
      tableTitle={t("nav.additionalSalary")}
      count={rows.length}
      toolbar={can(me, "payroll.additional-salary.manage") && <button onClick={() => void navigate({ to: "/payroll/additional-salary/new" })}>{t("ui.create")}</button>}
    >
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("fields.number")}</th><th>{t("additional.month")}</th><th>{t("fields.component")}</th><th>{t("fields.type")}</th><th className="num">{t("nav.employees")}</th><th className="num">{t("additional.total")}</th><th>{t("fields.status")}</th></tr></thead>
          <tbody>
            {rows.map((a) => (
              <tr key={a.id} className="navigable" onClick={() => void navigate({ to: "/payroll/additional-salary/$id", params: { id: a.id } })}>
                <td data-label={t("fields.number")} className="mono"><span className="strong-link">{a.number}</span></td>
                <td data-label={t("additional.month")}>{a.payrollDate.slice(0, 7)}</td>
                <td data-label={t("fields.component")}>{a.component} <span className="muted mono small">{a.abbreviation}</span></td>
                <td data-label={t("fields.type")}>{t(`slips.lineType.${a.type}`)}</td>
                <td data-label={t("nav.employees")} className="num">{a.lines.length}</td>
                <td data-label={t("additional.total")} className="num">{formatMoney(a.total)}</td>
                <td data-label={t("fields.status")}><ObjectStatus tone={statusTone(a.status)}>{t(`status.${a.status}`, a.status)}</ObjectStatus></td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <div className="empty">{list.isPending ? t("common.loading") : t("common.empty")}</div>}
    </ListReport>
  );
}

/** A new additional salary: one component for one payroll month, amounts per employee, typed or pasted from a spreadsheet. */
export function AdditionalSalaryCreate() {
  const { t } = useTranslation();
  const { company } = useSession();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const employees = useEmployees();
  const [type, setType] = useState<"Earning" | "Deduction">("Earning");
  const [amounts, setAmounts] = useState<Record<string, string>>({});
  const [pasted, setPasted] = useState("");
  const [problems, setProblems] = useState<string[]>([]);
  const active = useMemo(() => (employees.data ?? []).filter((e) => e.status !== "Left"), [employees.data]);
  const lines = Object.entries(amounts).filter(([, v]) => Number(v) > 0).map(([employeeId, v]) => ({ employeeId, amount: Number(v), note: null }));
  const total = lines.reduce((s, l) => s + l.amount, 0);
  const create = useMutation({
    mutationFn: (body: Record<string, string>) => post<AdditionalSalaryDto>("/api/payroll/additional-salaries", {
      companyId: company!.id, payrollDate: body.payrollDate, component: body.component, abbreviation: body.abbreviation, type,
      isTaxable: type === "Earning" && body.isTaxable === "on", lines, remark: body.remark || null,
    }),
    onSuccess: (a) => { void queryClient.invalidateQueries({ queryKey: ["additional-salaries"] }); void navigate({ to: "/payroll/additional-salary/$id", params: { id: a.id } }); },
  });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const applyPaste = () => {
    const { rows, problems: unread } = parsePastedRows(pasted, active);
    setAmounts((a) => ({ ...a, ...Object.fromEntries(rows.map((r) => [r.employeeId, String(r.amount)])) }));
    setProblems(unread);
    if (unread.length === 0) setPasted("");
  };
  const onSubmit = (e: FormEvent<HTMLFormElement>) => { e.preventDefault(); create.mutate(Object.fromEntries(new FormData(e.currentTarget)) as Record<string, string>); };

  return (
    <DynamicPage
      title={t("additional.new")}
      breadcrumb={<Link to="/payroll/additional-salary">{t("nav.additionalSalary")}</Link>}
      footer={<>
        {create.isError && <span className="error">{create.error.message}</span>}
        <span className="muted">{t("additional.summary", { count: lines.length, total: formatMoney(total) })}</span>
        <button type="submit" form="additional-create" disabled={create.isPending || lines.length === 0}>{t("common.save")}</button>
        <button className="secondary" onClick={() => void navigate({ to: "/payroll/additional-salary" })}>{t("common.cancel")}</button>
      </>}
    >
      <form id="additional-create" onSubmit={onSubmit}>
        <section className="object-section">
          <h2>{t("ui.general")}</h2>
          <div className="form-columns">
            <label>{t("fields.component")}<span className="required" aria-hidden>*</span><input name="component" required placeholder="Overtime" /></label>
            <label>{t("structures.abbreviation")}<span className="required" aria-hidden>*</span><input name="abbreviation" required className="mono" placeholder="OT" maxLength={20} /></label>
            <label>{t("fields.type")}
              <select value={type} onChange={(e) => setType(e.target.value as "Earning" | "Deduction")}>
                <option value="Earning">{t("slips.lineType.Earning")}</option><option value="Deduction">{t("slips.lineType.Deduction")}</option>
              </select>
            </label>
            <label>{t("additional.month")}<span className="required" aria-hidden>*</span><input type="date" name="payrollDate" required defaultValue={monthStart(todayIso())} /></label>
            {type === "Earning" && <label className="check"><input type="checkbox" name="isTaxable" defaultChecked /> {t("structures.taxable")}</label>}
            <label>{t("acc.remark")}<input name="remark" /></label>
          </div>
          <p className="muted small">{t("additional.hint")}</p>
        </section>
      </form>
      <section className="object-section">
        <h2>{t("additional.paste")}</h2>
        <textarea rows={4} className="mono" value={pasted} onChange={(e) => setPasted(e.target.value)} placeholder={"EMP-00001\t1500\nEMP-00002\t750"} aria-label={t("additional.paste")} />
        <div className="inline-form"><button type="button" className="secondary" disabled={!pasted.trim()} onClick={applyPaste}>{t("additional.applyPaste")}</button></div>
        {problems.length > 0 && <MessageStrip tone="critical">{t("additional.unread", { lines: problems.join(" · ") })}</MessageStrip>}
      </section>
      <section className="object-section">
        <h2>{t("nav.employees")}</h2>
        <table className="table edit-table">
          <thead><tr><th>{t("fields.number")}</th><th>{t("fields.name")}</th><th className="num">{t("fields.amount")}</th></tr></thead>
          <tbody>
            {active.map((e) => (
              <tr key={e.id}>
                <td className="mono">{e.number}</td><td>{e.fullName}</td>
                <td><input aria-label={`${t("fields.amount")} ${e.fullName}`} className="num" type="number" min="0" step="0.01" value={amounts[e.id] ?? ""}
                  onChange={(ev) => setAmounts((a) => ({ ...a, [e.id]: ev.target.value }))} /></td>
              </tr>
            ))}
          </tbody>
          <tfoot><tr><td colSpan={2}>{t("additional.total")}</td><td className="num">{formatMoney(total)}</td></tr></tfoot>
        </table>
      </section>
    </DynamicPage>
  );
}

/** One additional salary: its amounts, and submit (each employee's slip for the month takes them) or cancel. */
export function AdditionalSalaryDetail() {
  const { t } = useTranslation();
  const { me } = useSession();
  const queryClient = useQueryClient();
  const { id } = useParams({ from: "/payroll/additional-salary/$id" });
  const doc = useQuery({ queryKey: ["additional-salary", id], queryFn: () => api<AdditionalSalaryDto>(`/api/payroll/additional-salaries/${id}`) });
  const act = useMutation({
    mutationFn: (action: "submit" | "cancel") => post<AdditionalSalaryDto>(`/api/payroll/additional-salaries/${id}/${action}`),
    onSuccess: (a) => { queryClient.setQueryData(["additional-salary", id], a); void queryClient.invalidateQueries({ queryKey: ["additional-salaries"] }); },
  });
  if (doc.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (doc.isError) return <MessageStrip tone="negative">{doc.error.message}</MessageStrip>;
  const a = doc.data;
  const manage = can(me, "payroll.additional-salary.manage");

  return (
    <ObjectPage
      breadcrumb={<Link to="/payroll/additional-salary">{t("nav.additionalSalary")}</Link>}
      title={`${a.component} · ${a.payrollDate.slice(0, 7)}`}
      subtitle={<span className="mono">{a.number}</span>}
      status={<ObjectStatus tone={statusTone(a.status)}>{t(`status.${a.status}`, a.status)}</ObjectStatus>}
      kpis={[
        { label: t("additional.total"), value: formatMoney(a.total) },
        { label: t("fields.type"), value: `${t(`slips.lineType.${a.type}`)}${a.type === "Earning" ? (a.isTaxable ? ` · ${t("structures.taxable")}` : ` · ${t("additional.notTaxable")}`) : ""}` },
        { label: t("additional.inSlips"), value: `${a.inSlips} / ${a.lines.length}` },
      ]}
      actions={manage && <>
        {a.status === "Draft" && <button onClick={() => act.mutate("submit")} disabled={act.isPending}>{t("slips.submit")}</button>}
        {a.status === "Submitted" && <button className="secondary" onClick={() => act.mutate("cancel")} disabled={act.isPending}>{t("additional.cancel")}</button>}
      </>}
      messages={<>
        {a.status === "Submitted" && <MessageStrip tone="information">{t("additional.submittedHint")}</MessageStrip>}
        {act.isError && <MessageStrip tone="negative">{act.error.message}</MessageStrip>}
      </>}
      sections={[{ id: "lines", title: t("nav.employees"), content: (
        <table className="table responsive">
          <thead><tr><th>{t("fields.number")}</th><th>{t("fields.name")}</th><th className="num">{t("fields.amount")}</th></tr></thead>
          <tbody>{a.lines.map((l) => <tr key={l.employeeId}><td className="mono">{l.employeeNumber}</td><td>{l.employeeName}</td><td className="num">{formatMoney(l.amount)}</td></tr>)}</tbody>
          <tfoot><tr><td colSpan={2}>{t("additional.total")}</td><td className="num">{formatMoney(a.total)}</td></tr></tfoot>
        </table>
      ) },
        ...relatedSection(t, id, a.number),
        ...attachmentsSection(t, me, id),
        ...historySection(t, me, id, "AdditionalSalary")
      ]}
    />
  );
}
