import { useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Link, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useQueryClient } from "@tanstack/react-query";
import { useSession } from "../../../app/context";
import { api, post } from "../../../lib/api";
import { formatDate, formatMoney } from "../../../lib/ethiopian";
import { can, type JournalEntryDto } from "../../../lib/types";
import { Fields, MessageStrip, ObjectPage, ObjectStatus, statusTone } from "../../../ui/fiori";
import { historySection } from "../../../app/history";
import { attachmentsSection } from "../../../app/attachments";
import { relatedSection } from "../../../app/related";

const CANCELLABLE = ["Manual", "Opening", "YearEnd", "Bank"];

/** Object page of one journal entry. */
export function JournalDetail() {
  const { t, i18n } = useTranslation();
  const { me } = useSession();
  const queryClient = useQueryClient();
  const { id } = useParams({ from: "/accounting/journals/$id" });
  const [notice, setNotice] = useState<string | null>(null);
  const journal = useQuery({ queryKey: ["journals", "one", id], queryFn: () => api<JournalEntryDto>(`/api/accounting/journals/${id}`) });
  const act = useMutation({
    mutationFn: (action: "submit" | "cancel") => post<JournalEntryDto>(`/api/accounting/journals/${id}/${action}`),
    onSuccess: (j) => {
      setNotice(t(j.status === "Cancelled" ? "acc.journalCancelled" : "acc.journalPosted", { number: j.number }));
      for (const key of ["journals", "trial-balance", "financial", "bank-rec"]) void queryClient.invalidateQueries({ queryKey: [key] });
    },
  });
  if (journal.isError) return <MessageStrip tone="negative">{journal.error.message}</MessageStrip>;
  if (!journal.data) return <p className="muted">{t("common.loading")}</p>;
  const j = journal.data;
  const am = i18n.language === "am";

  return (
    <ObjectPage
      breadcrumb={<Link to="/accounting/journals">{t("nav.journals")}</Link>}
      title={j.number ?? t("status.Draft")}
      subtitle={j.remark}
      status={<ObjectStatus tone={statusTone(j.status)}>{t(`status.${j.status}`)}</ObjectStatus>}
      kpis={[
        { label: t("acc.debit"), value: formatMoney(j.totalDebit) },
        { label: t("acc.credit"), value: formatMoney(j.totalCredit), tone: j.totalDebit === j.totalCredit ? undefined : "negative" },
        { label: t("attendance.date"), value: formatDate(j.postingDate, me.calendar, am) },
        { label: t("acc.source"), value: t(`acc.sources.${j.source}`) },
      ]}
      actions={<>
        <Link to="/accounting/journals/$id/voucher" params={{ id: j.id }} className="button secondary">{t("voucher.print")}</Link>
        {j.status === "Draft" && can(me, "accounting.journals.submit") && <button onClick={() => act.mutate("submit")} disabled={act.isPending}>{t("slips.submit")}</button>}
        {j.status === "Submitted" && CANCELLABLE.includes(j.source) && can(me, "accounting.journals.cancel") &&
          <button className="secondary" onClick={() => act.mutate("cancel")} disabled={act.isPending}>{t("common.cancel")}</button>}
      </>}
      messages={<>
        {notice && <MessageStrip tone="positive" onClose={() => setNotice(null)}>{notice}</MessageStrip>}
        {act.isError && <MessageStrip tone="negative">{act.error.message}</MessageStrip>}
        {j.status === "Submitted" && !CANCELLABLE.includes(j.source) && <MessageStrip tone="information">{t("acc.fromSource", { source: t(`acc.sources.${j.source}`), number: j.sourceNumber })}</MessageStrip>}
      </>}
      sections={[
        { id: "general", title: t("ui.general"), content: (
          <Fields items={[
            { label: t("acc.remark"), value: j.remark },
            { label: t("attendance.date"), value: formatDate(j.postingDate, me.calendar, am) },
            { label: t("acc.source"), value: t(`acc.sources.${j.source}`) },
            { label: t("acc.sourceDocument"), value: j.sourceNumber && <span className="mono">{j.sourceNumber}</span>, hidden: !j.sourceNumber },
          ]} />
        ) },
        { id: "lines", title: t("ui.lines"), content: (
          <table className="table">
            <thead><tr><th>{t("acc.code")}</th><th>{t("fields.name")}</th><th>{t("cash.description")}</th><th className="num">{t("acc.debit")}</th><th className="num">{t("acc.credit")}</th></tr></thead>
            <tbody>{j.lines.map((l, i) => (
              <tr key={i}><td className="mono">{l.accountCode}</td><td>{l.accountName}</td><td className="muted">{l.description}</td>
                <td className="num">{l.debit ? formatMoney(l.debit) : ""}</td><td className="num">{l.credit ? formatMoney(l.credit) : ""}</td></tr>
            ))}</tbody>
            <tfoot><tr><td colSpan={3}>{t("common.total")}</td><td className="num">{formatMoney(j.totalDebit)}</td><td className="num">{formatMoney(j.totalCredit)}</td></tr></tfoot>
          </table>
        ) },
        ...relatedSection(t, id, j.number),
        ...attachmentsSection(t, me, id),
        ...historySection(t, me, id, "JournalEntry")
      ]}
    />
  );
}
