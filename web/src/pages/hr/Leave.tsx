import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatDate, toEthiopian, todayIso } from "../../lib/ethiopian";
import { formData, useEmployees } from "../../lib/hooks";
import { can, type LeaveApplicationDto, type LeaveBalanceDto } from "../../lib/types";
import { currentMonth, monthRange } from "../payroll/month";

const TYPES = ["Annual", "Sick", "Maternity", "Paternity", "Marriage", "Bereavement", "Unpaid"];

export function Leave() {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const employees = useEmployees();
  const [month, setMonth] = useState(currentMonth());
  const [employeeId, setEmployeeId] = useState("");
  const [message, setMessage] = useState<string | null>(null);
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

  const apply = useMutation({ mutationFn: (body: Record<string, string>) => post<LeaveApplicationDto>("/api/hr/leave-applications", body), onSuccess: refresh });
  const act = useMutation({
    mutationFn: ({ id, action }: { id: string; action: "approve" | "cancel" }) => post<LeaveApplicationDto>(`/api/hr/leave-applications/${id}/${action}`),
    onSuccess: refresh,
  });
  const allocate = useMutation({
    mutationFn: () => post<{ allocated: number; totalDays: number }>("/api/hr/leave-allocations", { companyId: company!.id, fiscalYear }),
    onSuccess: (r) => {
      setMessage(t("leave.allocated", { count: r.allocated, days: r.totalDays }));
      refresh();
    },
  });

  if (!company) return <p>{t("common.noCompany")}</p>;
  const onApply = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    apply.mutate(formData(event.currentTarget));
  };

  return (
    <>
      <div className="page-head">
        <h1>{t("nav.leave")}</h1>
        <label className="field-inline"><span>{t("common.month")}</span><input type="month" value={month} onChange={(ev) => setMonth(ev.target.value)} /></label>
      </div>
      {message && <div className="notice">{message}</div>}

      {can(me, "hr.leave.apply") && (
        <form className="form-grid" onSubmit={onApply}>
          <label>{t("fields.employee")}
            <select name="employeeId" required value={employeeId} onChange={(ev) => setEmployeeId(ev.target.value)}>
              <option value="">—</option>
              {employees.data?.map((x) => <option key={x.id} value={x.id}>{x.number} · {x.fullName}</option>)}
            </select>
          </label>
          <label>{t("leave.type")}<select name="type">{TYPES.map((x) => <option key={x} value={x}>{t(`leave.types.${x}`)}</option>)}</select></label>
          <label>{t("leave.from")}<input type="date" name="fromDate" required /></label>
          <label>{t("leave.to")}<input type="date" name="toDate" required /></label>
          <label>{t("leave.reason")}<input name="reason" /></label>
          <div className="form-actions">
            <button type="submit" disabled={apply.isPending}>{t("leave.apply")}</button>
            {apply.isError && <span className="error">{apply.error.message}</span>}
          </div>
        </form>
      )}

      {employeeId && balance.data && (
        <div className="cards">
          {balance.data.map((b) => (
            <div className="card" key={b.type}>
              <div className="card-label">{t(`leave.types.${b.type}`)}</div>
              <div className="card-value">{b.type === "Annual" ? b.balance : b.taken}</div>
              <div className="card-sub">{b.type === "Annual" ? `${t("leave.balance")} · ${b.allocated} / ${b.taken}` : t("leave.days")}</div>
            </div>
          ))}
        </div>
      )}

      {act.isError && <div className="error">{act.error.message}</div>}
      {leave.data?.length ? (
        <table className="table">
          <thead>
            <tr><th>{t("fields.number")}</th><th>{t("fields.employee")}</th><th>{t("leave.type")}</th><th>{t("leave.from")}</th><th>{t("leave.to")}</th>
              <th className="num">{t("leave.days")}</th><th>{t("fields.status")}</th><th /></tr>
          </thead>
          <tbody>
            {leave.data.map((l) => (
              <tr key={l.id}>
                <td className="mono">{l.number}</td><td>{l.employeeName}</td>
                <td>{t(`leave.types.${l.type}`)} <span className="muted">· {l.isPaid ? t("leave.paid") : t("leave.unpaid")}</span></td>
                <td>{formatDate(l.fromDate, me.calendar, am)}</td><td>{formatDate(l.toDate, me.calendar, am)}</td>
                <td className="num">{l.days}</td><td><span className={`badge ${l.status}`}>{t(`status.${l.status}`)}</span></td>
                <td>
                  {l.status === "Draft" && can(me, "hr.leave.approve") && <button onClick={() => act.mutate({ id: l.id, action: "approve" })}>{t("leave.approve")}</button>}
                  {l.status === "Submitted" && can(me, "hr.leave.approve") && <button className="secondary" onClick={() => act.mutate({ id: l.id, action: "cancel" })}>{t("leave.cancel")}</button>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <p className="muted">{t("common.empty")}</p>}

      {can(me, "hr.leave.allocate") && (
        <>
          <h2>{t("leave.allocate")}</h2>
          <div className="steps">
            <label className="field-inline"><span>{t("leave.fiscalYear")}</span>
              <input type="number" value={fiscalYear} onChange={(ev) => setFiscalYear(Number(ev.target.value))} style={{ width: 100 }} /></label>
            <button className="secondary" onClick={() => allocate.mutate()} disabled={allocate.isPending}>{t("leave.allocate")}</button>
            {allocate.isError && <span className="error">{allocate.error.message}</span>}
          </div>
        </>
      )}
    </>
  );
}
