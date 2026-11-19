import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { recordPath } from "../app/recordPages";
import { api, post } from "../lib/api";
import type { NotificationDto, NotificationPageDto } from "../lib/types";
import { ListReport, MessageStrip, ObjectStatus } from "../ui/fiori";
import { severityTone } from "./inboxModel";

const PAGE_SIZE = 20;

/** The signed-in user's own notifications, newest first. Opening one marks it read and goes to the record it is about. */
export function Inbox() {
  const { t, i18n } = useTranslation();
  const queryClient = useQueryClient();
  const [unreadOnly, setUnreadOnly] = useState(false);
  const [page, setPage] = useState(1);
  const inbox = useQuery({
    queryKey: ["notifications", "list", unreadOnly, page],
    queryFn: () => api<NotificationPageDto>(`/api/notifications?unread=${unreadOnly}&page=${page}&pageSize=${PAGE_SIZE}`),
  });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ["notifications"] });
  const markRead = useMutation({ mutationFn: (id: string) => post<NotificationDto>(`/api/notifications/${id}/read`), onSuccess: refresh });
  const markAll = useMutation({ mutationFn: () => post("/api/notifications/read-all"), onSuccess: refresh });
  const when = (iso: string) => new Date(iso).toLocaleString(i18n.language === "am" ? "am-ET" : "en-GB", { dateStyle: "medium", timeStyle: "short" });
  const rows = inbox.data?.items ?? [];
  const pages = Math.max(1, Math.ceil((inbox.data?.total ?? 0) / PAGE_SIZE));

  return (
    <ListReport title={t("inbox.title")} tableTitle={t("inbox.title")} count={inbox.data?.total}
      toolbar={<>
        <label className="inline-check"><input type="checkbox" checked={unreadOnly} onChange={(e) => { setUnreadOnly(e.target.checked); setPage(1); }} /> {t("inbox.unreadOnly")}</label>
        <button className="secondary" disabled={markAll.isPending || !inbox.data?.unread} onClick={() => markAll.mutate()}>{t("inbox.markAll")}</button>
      </>}
      messages={(inbox.isError || markRead.isError || markAll.isError) && <MessageStrip tone="negative">{(inbox.error ?? markRead.error ?? markAll.error)?.message}</MessageStrip>}>
      {inbox.isPending ? <p className="muted">{t("common.loading")}</p> : rows.length === 0 ? <div className="empty">{t(unreadOnly ? "inbox.noneUnread" : "inbox.none")}</div> : (
        <ul className="inbox-list">
          {rows.map((n) => {
            const to = recordPath(n.linkKind, n.linkId);
            return (
              <li key={n.id} className={n.readAt ? "inbox-item" : "inbox-item unread"}>
                <div>
                  <ObjectStatus tone={severityTone(n.severity)}>{t(`inbox.severity.${n.severity}`, n.severity)}</ObjectStatus>{" "}
                  <strong>{n.title}</strong>
                  {n.body && <p className="muted small">{n.body}</p>}
                  <span className="muted small">{when(n.createdAt)}</span>
                </div>
                <div className="inbox-actions">
                  {to && <Link to={to as never} onClick={() => { if (!n.readAt) markRead.mutate(n.id); }}>{t("inbox.open")}</Link>}
                  {!n.readAt && <button className="link-button" disabled={markRead.isPending} onClick={() => markRead.mutate(n.id)}>{t("inbox.markRead")}</button>}
                </div>
              </li>
            );
          })}
        </ul>
      )}
      {pages > 1 && (
        <div className="toolbar-actions">
          <button className="secondary" disabled={page <= 1} onClick={() => setPage(page - 1)}>{t("inbox.previous")}</button>
          <span className="muted small">{page} / {pages}</span>
          <button className="secondary" disabled={page >= pages} onClick={() => setPage(page + 1)}>{t("inbox.next")}</button>
        </div>
      )}
    </ListReport>
  );
}
