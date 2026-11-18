import { useQuery } from "@tanstack/react-query";
import type { TFunction } from "i18next";
import { useTranslation } from "react-i18next";
import { api } from "../lib/api";
import { can, type MeDto } from "../lib/types";
import type { Section } from "../ui/fiori";

type ChangeRecord = { entityId: string; entityType: string; at: string; by: string; action: "Created" | "Changed" | "Deleted"; field?: string; oldValue?: string; newValue?: string };

/** "BankAccountNumber" → "Bank account number". */
const words = (field: string) => field.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/^./, (c) => c.toUpperCase()).replace(/ ([A-Z])(?=[a-z])/g, (_, c: string) => ` ${c.toLowerCase()}`);

/** Who changed what and when on this record, newest first (GET /api/history/{id}); changes to parts kept elsewhere, such as payroll details, are marked. */
export function History({ id, entityType }: { id: string; entityType?: string }) {
  const { t, i18n } = useTranslation();
  const history = useQuery({ queryKey: ["history", id], queryFn: () => api<ChangeRecord[]>(`/api/history/${id}`) });
  if (history.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (history.isError) return <p className="error">{history.error.message}</p>;
  if (history.data.length === 0) return <div className="empty">{t("history.none")}</div>;
  const when = (iso: string) => new Date(iso).toLocaleString(i18n.language === "am" ? "am-ET" : "en-GB", { dateStyle: "medium", timeStyle: "short" });

  return (
    <table className="table responsive">
      <thead><tr><th>{t("history.when")}</th><th>{t("history.who")}</th><th>{t("history.what")}</th><th>{t("history.from")}</th><th>{t("history.to")}</th></tr></thead>
      <tbody>
        {history.data.map((h, i) => (
          <tr key={i}>
            <td data-label={t("history.when")} className="small">{when(h.at)}</td>
            <td data-label={t("history.who")}>{h.by}</td>
            <td data-label={t("history.what")}>
              {h.action === "Changed" ? words(h.field ?? "") : t(`history.${h.action}`)}
              {entityType && h.entityType !== entityType && <span className="muted small"> · {words(h.entityType)}</span>}
            </td>
            <td data-label={t("history.from")} className="small">{h.action === "Changed" ? (h.oldValue ?? (h.newValue === undefined ? "" : "—")) : ""}</td>
            <td data-label={t("history.to")} className="small">{h.action === "Changed" ? (h.newValue ?? (h.oldValue === undefined ? t("history.listChanged") : "—")) : ""}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** The History section for an object page, when the user may read history; spread it at the end of the page's sections. */
export function historySection(t: TFunction, me: MeDto, id: string | undefined, entityType?: string): Section[] {
  return id && can(me, "core.history.read") ? [{ id: "history", title: t("history.title"), content: <History id={id} entityType={entityType} /> }] : [];
}
