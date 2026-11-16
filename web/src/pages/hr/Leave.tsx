import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatDate, toEthiopian, todayIso } from "../../lib/ethiopian";
import { formData, useEmployees } from "../../lib/hooks";
import { can, type LeaveApplicationDto, type LeaveBalanceDto } from "../../lib/types";
import { currentMonth, monthRange } from "../payroll/month";
import { Dialog, FilterBar, FilterField, ListReport, MessageStrip, ObjectStatus, statusTone } from "../../ui/fiori";

const TYPES = ["Annual", "Sick", "Maternity", "Paternity", "Marriage", "Bereavement", "Unpaid"];

export function Leave() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const employees = useEmployees();
  const [month, setMonth] = useState(currentMonth());
  const [employeeId, setEmployeeId] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [dialog, setDialog] = useState<"apply" | "allocate" | null>(null);
  const [status, setStatus] = useState("");
  const { start, end } = monthRange(month);
  const am = i18n.language === "am";
  const e = toEthiopian(todayIso());
  const [fiscalYear, setFiscalYear] = useState(e.month >= 11 ? e.year + 1 : e.year);

  const leave = useQuery({
    queryKey: ["leave", company?.id, month],
    queryFn: () => api<LeaveApplicationDto[]>(`/api/hr/leave-applications?companyId=${company!.id}&from=${start}&to=${end}`),
    enabled: !!company,
  });
  const balance = useQuery({
    queryKey: ["leave-balance", employeeId],
    queryFn: () => api<LeaveBalanceDto[]>(`/api/hr/employees/${employeeId}/leave-balance`),
    enabled: !!employeeId,
  });
  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ["leave"] });
    void queryClient.invalidateQueries({ queryKey: ["leave-balance"] });
  };

  const apply = useMutation({ mutationFn: (body: Record<string, string>) => post<LeaveApplicationDto>("/api/hr/leave-applications", body),
    onSuccess: (l) => { setDialog(null); setMessage(t("leave.applied", { number: l.number, days: l.days })); refresh(); } });
  const act = useMutation({
    mutationFn: ({ id, action }: { id: string; action: "approve" | "cancel" }) => post<LeaveApplicationDto>(`/api/hr/leave-applications/${id}/${action}`),
    onSuccess: refresh,
  });
  const allocate = useMutation({
    mutationFn: () => post<{ allocated: number; totalDays: number }>("/api/hr/leave-allocations", { companyId: company!.id, fiscalYear }),
    onSuccess: (r) => {
      setDialog(null);
      setMessage(t("leave.allocated", { count: r.allocated, days: r.totalDays }));
      refresh();
    },
  });

  if (!company) return <p>{t("common.noCompany")}</p>;
  const onApply = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    apply.mutate(formData(event.currentTarget));
  };

  const rows = (leave.data ?? []).filter((l) => (!employeeId || l.employeeId === employeeId) && (!status || l.status === status));
  const footer = (form: string, pending: boolean, label: string) => (
    <><button type="submit" form={form} disabled={pending}>{label}</button><button className="secondary" onClick={() => setDialog(null)}>{t("common.cancel")}</button></>
  );

  return (
    <ListReport
      title={t("nav.leave")}
      filters={
        <FilterBar>
          <FilterField label={t("fields.employee")}>
            <select value={employeeId} onChange={(ev) => setEmployeeId(ev.target.value)}>
              <option value="">{t("ui.all")}</option>
              {employees.data?.map((x) => <option key={x.id} value={x.id}>{x.number} · {x.fullName}</option>)}
            </select>
          </FilterField>
          <FilterField label={t("fields.status")}>
            <select value={status} onChange={(ev) => setStatus(ev.target.value)}>
              <option value="">{t("ui.all")}</option>
              {["Draft", "Submitted", "Cancelled"].map((x) => <option key={x} value={x}>{t(`status.${x}`)}</option>)}
            </select>
          </FilterField>
          <FilterField label={t("common.month")}><input type="month" value={month} onChange={(ev) => setMonth(ev.target.value)} /></FilterField>
        </FilterBar>
      }
      tableTitle={t("leave.applications")}
      count={rows.length}
      messages={<>
        {message && <MessageStrip tone="positive" onClose={() => setMessage(null)}>{message}</MessageStrip>}
        {act.isError && <MessageStrip tone="negative">{act.error.message}</MessageStrip>}
        {employeeId && balance.data && (
          <section className="object-section">
            <h2>{t("leave.balance")} · {employees.data?.find((x) => x.id === employeeId)?.fullName}</h2>
            <div className="kpis">
              {balance.data.map((b) => (
                <div className="kpi" key={b.type}>
                  <div className="kpi-label">{t(`leave.types.${b.type}`)}</div>
                  <div className="kpi-value">{b.type === "Annual" ? b.balance : b.taken} <span className="muted small">{t("leave.days")}</span></div>
                  {b.type === "Annual" && <div className="muted small">{b.allocated} − {b.taken}</div>}
                </div>
              ))}
            </div>
          </section>
        )}
      </>}
      toolbar={<>
        {can(me, "hr.leave.allocate") && <button className="secondary" onClick={() => setDialog("allocate")}>{t("leave.allocate")}</button>}
        {can(me, "hr.leave.apply") && <button onClick={() => setDialog("apply")}>{t("leave.apply")}</button>}
      </>}
    >
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("fields.number")}</th><th>{t("fields.employee")}</th><th>{t("leave.type")}</th><th>{t("leave.from")}</th><th>{t("leave.to")}</th>
            <th className="num">{t("leave.days")}</th><th>{t("fields.status")}</th><th /></tr></thead>
          <tbody>
            {rows.map((l) => (
              <tr key={l.id}>
                <td data-label={t("fields.number")} className="mono">{l.number}</td>
                <td data-label={t("fields.employee")}>{l.employeeName}</td>
                <td data-label={t("leave.type")}>{t(`leave.types.${l.type}`)} <span className="muted small">· {l.isPaid ? t("leave.paid") : t("leave.unpaid")}</span></td>
                <td data-label={t("leave.from")}>{formatDate(l.fromDate, me.calendar, am)}</td>
                <td data-label={t("leave.to")}>{formatDate(l.toDate, me.calendar, am)}</td>
                <td data-label={t("leave.days")} className="num">{l.days}</td>
                <td data-label={t("fields.status")}><ObjectStatus tone={statusTone(l.status)}>{t(`status.${l.status}`)}</ObjectStatus></td>
                <td className="actions">
                  {l.status === "Draft" && can(me, "hr.leave.approve") && <button onClick={() => act.mutate({ id: l.id, action: "approve" })} disabled={act.isPending}>{t("leave.approve")}</button>}
                  {l.status === "Submitted" && can(me, "hr.leave.approve") && <button className="secondary" onClick={() => act.mutate({ id: l.id, action: "cancel" })} disabled={act.isPending}>{t("leave.cancel")}</button>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <div className="empty">{leave.isPending ? t("common.loading") : t("common.empty")}</div>}

      <Dialog open={dialog === "apply"} title={t("leave.apply")} onClose={() => setDialog(null)} footer={footer("leave-apply", apply.isPending, t("leave.apply"))}>
        <form id="leave-apply" className="dialog-form" onSubmit={onApply}>
          <label>{t("fields.employee")}<span className="required" aria-hidden>*</span>
            <select name="employeeId" required defaultValue={employeeId}>
              <option value="">—</option>
              {employees.data?.map((x) => <option key={x.id} value={x.id}>{x.number} · {x.fullName}</option>)}
            </select>
          </label>
          <label>{t("leave.type")}<select name="type">{TYPES.map((x) => <option key={x} value={x}>{t(`leave.types.${x}`)}</option>)}</select></label>
          <label>{t("leave.from")}<span className="required" aria-hidden>*</span><input type="date" name="fromDate" required /></label>
          <label>{t("leave.to")}<span className="required" aria-hidden>*</span><input type="date" name="toDate" required /></label>
          <label>{t("leave.reason")}<input name="reason" /></label>
          {apply.isError && <MessageStrip tone="negative">{apply.error.message}</MessageStrip>}
        </form>
      </Dialog>
      <Dialog open={dialog === "allocate"} title={t("leave.allocate")} onClose={() => setDialog(null)} footer={footer("leave-allocate", allocate.isPending, t("leave.allocate"))}>
        <form id="leave-allocate" className="dialog-form" onSubmit={(ev) => { ev.preventDefault(); allocate.mutate(); }}>
          <p className="muted small">{t("leave.allocateHint")}</p>
          <label>{t("leave.fiscalYear")}<input type="number" value={fiscalYear} onChange={(ev) => setFiscalYear(Number(ev.target.value))} /></label>
          {allocate.isError && <MessageStrip tone="negative">{allocate.error.message}</MessageStrip>}
        </form>
      </Dialog>
    </ListReport>
  );
}
