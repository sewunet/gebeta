import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../app/context";
import { api, post, put } from "../lib/api";
import { can, type AlertConditionDto, type AlertRuleDto, type AlertRunDto } from "../lib/types";
import { ListReport, MessageStrip, ObjectStatus } from "../ui/fiori";
import { conditionKey, parseNames } from "./alertModel";

/** Alert rules of the selected company: a condition a module offers, who is told, and a threshold. They run once a day, or now. */
export function Alerts() {
  const { t } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const manage = can(me, "core.alerts.manage");
  const conditions = useQuery({ queryKey: ["alert-conditions"], queryFn: () => api<AlertConditionDto[]>("/api/core/alerts/conditions") });
  const rules = useQuery({ queryKey: ["alert-rules", company?.id], queryFn: () => api<AlertRuleDto[]>(`/api/core/alerts/rules?companyId=${company!.id}`), enabled: !!company });
  const [conditionId, setConditionId] = useState("");
  const [lastRun, setLastRun] = useState<AlertRunDto | null>(null);
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ["alert-rules"] });
  const create = useMutation({ mutationFn: (b: object) => post<AlertRuleDto>("/api/core/alerts/rules", { companyId: company!.id, ...b }), onSuccess: refresh });
  const toggle = useMutation({ mutationFn: (r: AlertRuleDto) => post<AlertRuleDto>(`/api/core/alerts/rules/${r.id}/${r.isEnabled ? "disable" : "enable"}`), onSuccess: refresh });
  const run = useMutation({ mutationFn: (r: AlertRuleDto) => post<AlertRunDto>(`/api/core/alerts/rules/${r.id}/run`), onSuccess: (d) => { setLastRun(d); refresh(); } });
  const remove = useMutation({ mutationFn: (r: AlertRuleDto) => api(`/api/core/alerts/rules/${r.id}`, { method: "DELETE" }), onSuccess: refresh });
  const [editing, setEditing] = useState<string | null>(null);
  const update = useMutation({
    mutationFn: (b: { id: string; name: string; threshold?: number; userNames: string[]; roleNames: string[] }) => put<AlertRuleDto>(`/api/core/alerts/rules/${b.id}`, b),
    onSuccess: () => { setEditing(null); refresh(); },
  });
  if (!company) return <p>{t("common.noCompany")}</p>;

  const list = conditions.data ?? [];
  const byId = new Map(list.map((c) => [c.id, c]));
  const nameOf = (id: string) => t(`alerts.conditions.${conditionKey(id)}`, { defaultValue: byId.get(id)?.name ?? id });
  const chosen = byId.get(conditionId);
  const rows = rules.data ?? [];
  const unit = (c?: AlertConditionDto) => (c?.thresholdUnit ? t(`alerts.unit.${c.thresholdUnit}`, c.thresholdUnit) : "");

  const onCreate = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    const threshold = chosen?.thresholdUnit && String(f.get("threshold")) !== "" ? Number(f.get("threshold")) : undefined;
    create.mutate({ conditionId, name: String(f.get("name") ?? "").trim() || undefined, threshold, userNames: parseNames(String(f.get("users") ?? "")), roleNames: parseNames(String(f.get("roles") ?? "")) });
  };
  const onUpdate = (e: FormEvent<HTMLFormElement>, r: AlertRuleDto) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    update.mutate({ id: r.id, name: String(f.get("name")), threshold: r.threshold === undefined ? undefined : Number(f.get("threshold")), userNames: parseNames(String(f.get("users"))), roleNames: parseNames(String(f.get("roles"))) });
  };
  const errors = [rules.error, create.error, toggle.error, run.error, remove.error, update.error].find(Boolean);

  return (
    <ListReport title={t("nav.alerts")} tableTitle={t("nav.alerts")} count={rows.length}
      messages={<>
        <MessageStrip tone="information">{t("alerts.explain")}</MessageStrip>
        {errors && <MessageStrip tone="negative">{errors.message}</MessageStrip>}
        {lastRun && <MessageStrip tone={lastRun.error ? "critical" : "positive"} onClose={() => setLastRun(null)}>
          {lastRun.skipped ? t("alerts.skipped") : t("alerts.ran", { matched: lastRun.matched, notified: lastRun.notified, cleared: lastRun.cleared })}{lastRun.error ? ` ${lastRun.error}` : ""}</MessageStrip>}
      </>}>
      {list.length === 0 && <p className="muted">{t("alerts.noConditions")}</p>}
      <table className="table responsive">
        <thead><tr><th>{t("fields.name")}</th><th>{t("alerts.condition")}</th><th>{t("alerts.threshold")}</th><th>{t("alerts.recipients")}</th><th>{t("alerts.lastRun")}</th><th>{t("fields.status")}</th>{manage && <th />}</tr></thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.id}>
              <td data-label={t("fields.name")}>{editing === r.id
                ? <form id={`edit-${r.id}`} onSubmit={(e) => onUpdate(e, r)}><input name="name" defaultValue={r.name} required maxLength={120} aria-label={t("fields.name")} /></form>
                : r.name}</td>
              <td data-label={t("alerts.condition")}>{nameOf(r.conditionId)}{!r.conditionAvailable && <> <ObjectStatus tone="negative">{t("alerts.unavailable")}</ObjectStatus></>}</td>
              <td data-label={t("alerts.threshold")} className="num">{editing === r.id && r.threshold !== undefined
                ? <input form={`edit-${r.id}`} name="threshold" type="number" defaultValue={r.threshold} required aria-label={t("alerts.threshold")} />
                : r.threshold !== undefined ? `${r.threshold} ${unit(byId.get(r.conditionId))}` : "-"}</td>
              <td data-label={t("alerts.recipients")}>{editing === r.id
                ? <><input form={`edit-${r.id}`} name="users" defaultValue={r.userNames.join(", ")} placeholder={t("alerts.users")} aria-label={t("alerts.users")} />
                    <input form={`edit-${r.id}`} name="roles" defaultValue={r.roleNames.join(", ")} placeholder={t("alerts.roles")} aria-label={t("alerts.roles")} /></>
                : [...r.userNames, ...r.roleNames.map((n) => `${t("alerts.roleTag")} ${n}`)].join(", ")}</td>
              <td data-label={t("alerts.lastRun")}>{r.lastRunOn
                ? <>{r.lastRunOn} · {t("alerts.counts", { matched: r.lastMatchCount, notified: r.lastNotifiedCount })}{r.lastError && <div><ObjectStatus tone="negative">{r.lastError}</ObjectStatus></div>}</>
                : <span className="muted">{t("alerts.never")}</span>}</td>
              <td data-label={t("fields.status")}><ObjectStatus tone={r.isEnabled ? "positive" : "neutral"}>{t(r.isEnabled ? "alerts.on" : "alerts.off")}</ObjectStatus></td>
              {manage && <td className="num">
                {editing === r.id
                  ? <><button type="submit" form={`edit-${r.id}`} disabled={update.isPending}>{t("common.save")}</button>
                      <button type="button" className="secondary" onClick={() => setEditing(null)}>{t("common.cancel")}</button></>
                  : <>
                      {r.conditionAvailable && <button className="link-button" onClick={() => setEditing(r.id)}>{t("ui.edit")}</button>}
                      <button className="link-button" disabled={toggle.isPending} onClick={() => toggle.mutate(r)}>{t(r.isEnabled ? "alerts.switchOff" : "alerts.switchOn")}</button>
                      {r.isEnabled && <button className="link-button" disabled={run.isPending} onClick={() => run.mutate(r)}>{t("alerts.runNow")}</button>}
                      <button className="link-button" disabled={remove.isPending} onClick={() => { if (window.confirm(t("alerts.confirmDelete", { name: r.name }))) remove.mutate(r); }}>{t("alerts.delete")}</button>
                    </>}
              </td>}
            </tr>
          ))}
        </tbody>
      </table>
      {manage && list.length > 0 && (
        <form className="inline-form" onSubmit={onCreate}>
          <select value={conditionId} onChange={(e) => setConditionId(e.target.value)} required aria-label={t("alerts.condition")}>
            <option value="">{t("alerts.chooseCondition")}</option>
            {list.map((c) => <option key={c.id} value={c.id}>{nameOf(c.id)}</option>)}
          </select>
          <input name="name" maxLength={120} placeholder={t("fields.name")} aria-label={t("fields.name")} />
          {chosen?.thresholdUnit && <input name="threshold" type="number" min={chosen.minThreshold} max={chosen.maxThreshold} defaultValue={chosen.defaultThreshold} key={chosen.id} aria-label={`${t("alerts.threshold")} (${unit(chosen)})`} title={unit(chosen)} />}
          <input name="users" placeholder={t("alerts.users")} aria-label={t("alerts.users")} />
          <input name="roles" placeholder={t("alerts.roles")} aria-label={t("alerts.roles")} />
          <button type="submit" disabled={create.isPending || !conditionId}>{t("alerts.add")}</button>
        </form>
      )}
    </ListReport>
  );
}
