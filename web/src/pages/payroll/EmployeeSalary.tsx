import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api, post } from "../../lib/api";
import { formatDate, formatMoney, todayIso } from "../../lib/ethiopian";
import { useSalaryStructures } from "../../lib/queries";
import { can, type SalaryStructureAssignmentDto } from "../../lib/types";
import { Dialog, MessageStrip } from "../../ui/fiori";
import { formatValues } from "./structureForm";

/**
 * The employee's salary: every assignment (structure, base and pay values from a date; a raise is a new one) and a dialog for
 * the next. The dialog asks for the pay values the chosen structure uses.
 */
export function EmployeeSalary({ employeeId, joined }: { employeeId: string; joined: string }) {
  const { t, i18n } = useTranslation();
  const { me } = useSession();
  const queryClient = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [structureId, setStructureId] = useState("");
  const structures = useSalaryStructures();
  const assignments = useQuery({
    queryKey: ["assignments", employeeId],
    queryFn: () => api<SalaryStructureAssignmentDto[]>(`/api/payroll/employees/${employeeId}/assignments`),
  });
  const assign = useMutation({
    mutationFn: (body: object) => post<SalaryStructureAssignmentDto>("/api/payroll/assignments", body),
    onSuccess: () => { setAdding(false); void queryClient.invalidateQueries({ queryKey: ["assignments", employeeId] }); },
  });
  const chosen = structures.data?.find((s) => s.id === structureId);
  const nameOf = (id: string) => structures.data?.find((s) => s.id === id)?.name ?? "—";
  const rows = [...(assignments.data ?? [])].sort((a, b) => b.fromDate.localeCompare(a.fromDate));
  const onSubmit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    assign.mutate({
      employeeId, structureId, base: Number(f.get("base")), fromDate: String(f.get("fromDate")),
      values: Object.fromEntries((chosen?.variables ?? []).map((n) => [n, Number(f.get(`value-${n}`) || 0)])),
    });
  };

  return (
    <>
      {can(me, "payroll.assignments.manage") && (
        <div className="table-toolbar"><span /><button className="secondary" onClick={() => { assign.reset(); setAdding(true); }}>{t("structures.assign")}</button></div>
      )}
      {rows.length ? (
        <table className="table responsive">
          <thead><tr><th>{t("structures.from")}</th><th>{t("nav.salaryStructure")}</th><th className="num">{t("fields.base")}</th><th>{t("structures.payValues")}</th></tr></thead>
          <tbody>
            {rows.map((a, i) => (
              <tr key={a.id} className={i > 0 ? "muted" : ""}>
                <td data-label={t("structures.from")}>{formatDate(a.fromDate, me.calendar, i18n.language === "am")}</td>
                <td data-label={t("nav.salaryStructure")}><Link to="/payroll/structures/$id" params={{ id: a.structureId }}>{nameOf(a.structureId)}</Link></td>
                <td data-label={t("fields.base")} className="num">{formatMoney(a.base)}</td>
                <td data-label={t("structures.payValues")} className="mono small">{formatValues(a.values) || "—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : <div className="empty">{assignments.isPending ? t("common.loading") : t("structures.noAssignment")}</div>}

      <Dialog open={adding} title={t("structures.assign")} onClose={() => setAdding(false)}
        footer={<><button type="submit" form="assignment-form" disabled={assign.isPending || !structureId}>{t("common.save")}</button>
          <button className="secondary" onClick={() => setAdding(false)}>{t("common.cancel")}</button></>}>
        <form id="assignment-form" className="dialog-form" onSubmit={onSubmit}>
          <p className="muted small">{t("structures.assignHint")}</p>
          <label>{t("nav.salaryStructure")}<span className="required" aria-hidden>*</span>
            <select value={structureId} onChange={(e) => setStructureId(e.target.value)} required>
              <option value="">—</option>
              {structures.data?.filter((s) => s.isActive).map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
          </label>
          <label>{t("structures.from")}<span className="required" aria-hidden>*</span><input type="date" name="fromDate" min={joined} defaultValue={todayIso()} required /></label>
          <label>{t("fields.base")}<span className="required" aria-hidden>*</span><input type="number" name="base" min="0" step="0.01" required /></label>
          {chosen?.variables?.map((n) => (
            <label key={n}><span className="mono">{n}</span><span className="required" aria-hidden>*</span><input type="number" name={`value-${n}`} step="0.01" required /></label>
          ))}
          {assign.isError && <MessageStrip tone="negative">{assign.error.message}</MessageStrip>}
        </form>
      </Dialog>
    </>
  );
}
