import { useQuery } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import type { TFunction } from "i18next";
import { useTranslation } from "react-i18next";
import { api } from "../lib/api";
import { formatDate, formatMoney } from "../lib/ethiopian";
import type { Section } from "../ui/fiori";
import { useSession } from "./context";
import { RECORD_PAGE as PAGE } from "./recordPages";

type RelatedDocument = { type: string; id?: string; number?: string; date?: string; status?: string; amount?: number };

/** The documents before and after this one (order, receipt, invoice, payment, journal; loan or additional salary, salary slip), in date order. */
export function RelatedDocuments({ id, number }: { id: string; number?: string }) {
  const { t, i18n } = useTranslation();
  const { company, me } = useSession();
  const related = useQuery({
    queryKey: ["related", id, number, company?.id],
    queryFn: () => api<RelatedDocument[]>(`/api/documents/${id}/related?companyId=${company!.id}${number ? `&number=${encodeURIComponent(number)}` : ""}`),
    enabled: !!company,
  });
  if (related.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (related.isError) return <p className="error">{related.error.message}</p>;
  if (related.data.length === 0) return <div className="empty">{t("related.none")}</div>;
  const am = i18n.language === "am";

  return (
    <table className="table responsive">
      <thead><tr><th>{t("related.document")}</th><th>{t("fields.number")}</th><th>{t("attendance.date")}</th><th>{t("fields.status")}</th><th className="num">{t("cash.amount")}</th></tr></thead>
      <tbody>
        {related.data.map((d) => (
          <tr key={`${d.type}-${d.id ?? d.number}`}>
            <td data-label={t("related.document")}>{t(`related.type.${d.type}`, d.type)}</td>
            <td data-label={t("fields.number")} className="mono">
              {d.id && PAGE[d.type] ? <Link to={PAGE[d.type] as never} params={{ id: d.id } as never}>{d.number ?? "—"}</Link> : d.number}
            </td>
            <td data-label={t("attendance.date")}>{d.date ? formatDate(d.date, me.calendar, am) : ""}</td>
            <td data-label={t("fields.status")}>{d.status ? t(`status.${d.status}`, d.status) : ""}</td>
            <td data-label={t("cash.amount")} className="num">{d.amount == null ? "" : formatMoney(d.amount)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** The Related documents section for a document's object page; spread it before the History section. */
export function relatedSection(t: TFunction, id: string | undefined, number?: string | null): Section[] {
  return id ? [{ id: "related", title: t("related.title"), content: <RelatedDocuments id={id} number={number ?? undefined} /> }] : [];
}
