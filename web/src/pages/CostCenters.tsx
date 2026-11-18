import { useState, type FormEvent } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSession } from "../app/context";
import { post, put } from "../lib/api";
import { useCostCenters } from "../lib/queries";
import { can, type CostCenterDto } from "../lib/types";
import { ListReport, MessageStrip, ObjectStatus } from "../ui/fiori";

/**
 * The parts of the company that costs and income are reported for (an estate, a factory, head office). Main always exists and takes
 * every posting that names no other cost centre.
 */
export function CostCenters() {
  const { t } = useTranslation();
  const { me, company } = useSession();
  const queryClient = useQueryClient();
  const centers = useCostCenters();
  const [editing, setEditing] = useState<string | null>(null);
  const done = () => { setEditing(null); void queryClient.invalidateQueries({ queryKey: ["cost-centers"] }); };
  const create = useMutation({ mutationFn: (b: { code: string; name: string }) => post<CostCenterDto>("/api/core/cost-centers", { companyId: company!.id, ...b }), onSuccess: done });
  const update = useMutation({ mutationFn: (c: { id: string; name: string; isActive: boolean }) => put<CostCenterDto>(`/api/core/cost-centers/${c.id}`, c), onSuccess: done });
  if (!company) return <p>{t("common.noCompany")}</p>;
  const manage = can(me, "core.cost-centers.manage");
  const rows = centers.data ?? [];
  const onCreate = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    create.mutate({ code: String(f.get("code")), name: String(f.get("name")) });
    e.currentTarget.reset();
  };
  const onRename = (e: FormEvent<HTMLFormElement>, c: CostCenterDto) => {
    e.preventDefault();
    update.mutate({ id: c.id, name: String(new FormData(e.currentTarget).get("name")), isActive: c.isActive });
  };

  return (
    <ListReport title={t("nav.costCenters")} tableTitle={t("nav.costCenters")} count={rows.length}
      messages={<>
        <MessageStrip tone="information">{t("costCenters.explain")}</MessageStrip>
        {(create.isError || update.isError) && <MessageStrip tone="negative">{(create.error ?? update.error)?.message}</MessageStrip>}
      </>}>
      <table className="table responsive">
        <thead><tr><th>{t("acc.code")}</th><th>{t("fields.name")}</th><th>{t("fields.status")}</th>{manage && <th />}</tr></thead>
        <tbody>
          {rows.map((c) => (
            <tr key={c.id}>
              <td data-label={t("acc.code")} className="mono">{c.code}</td>
              <td data-label={t("fields.name")}>
                {editing === c.id
                  ? <form className="inline-form" onSubmit={(e) => onRename(e, c)}><input name="name" defaultValue={c.name} required maxLength={140} autoFocus />
                      <button type="submit" disabled={update.isPending}>{t("common.save")}</button>
                      <button type="button" className="secondary" onClick={() => setEditing(null)}>{t("common.cancel")}</button></form>
                  : <>{c.name} {c.isMain && <span className="muted small">· {t("costCenters.main")}</span>}</>}
              </td>
              <td data-label={t("fields.status")}><ObjectStatus tone={c.isActive ? "positive" : "neutral"}>{t(c.isActive ? "status.Active" : "status.Inactive")}</ObjectStatus></td>
              {manage && <td className="num">
                {editing !== c.id && <button className="link-button" onClick={() => setEditing(c.id)}>{t("ui.edit")}</button>}
                {!c.isMain && <button className="link-button" disabled={update.isPending} onClick={() => update.mutate({ id: c.id, name: c.name, isActive: !c.isActive })}>
                  {t(c.isActive ? "costCenters.deactivate" : "costCenters.activate")}</button>}
              </td>}
            </tr>
          ))}
        </tbody>
      </table>
      {manage && (
        <form className="inline-form" onSubmit={onCreate}>
          <input name="code" required maxLength={20} pattern="[A-Za-z0-9_\-]+" className="mono" placeholder="ESTATE" aria-label={t("acc.code")} />
          <input name="name" required maxLength={140} placeholder={t("costCenters.namePlaceholder")} aria-label={t("fields.name")} />
          <button type="submit" disabled={create.isPending}>{t("costCenters.add")}</button>
        </form>
      )}
    </ListReport>
  );
}
