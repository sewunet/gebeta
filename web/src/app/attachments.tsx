import { useRef } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { TFunction } from "i18next";
import { useTranslation } from "react-i18next";
import { api, download } from "../lib/api";
import { can, type MeDto } from "../lib/types";
import type { Section } from "../ui/fiori";
import { useSession } from "./context";

type AttachmentDto = { id: string; fileName: string; contentType: string; size: number; uploadedAt: string; uploadedBy?: string };

const size = (bytes: number) => (bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`);

/** The files attached to a record (a supplier's invoice scan, a court order, a contract): download, add (PDF, images, Office, CSV; 10 MB) and remove. */
export function Attachments({ id }: { id: string }) {
  const { t, i18n } = useTranslation();
  const { company, me } = useSession();
  const queryClient = useQueryClient();
  const input = useRef<HTMLInputElement>(null);
  const key = ["attachments", company?.id, id];
  const files = useQuery({ queryKey: key, queryFn: () => api<AttachmentDto[]>(`/api/attachments?companyId=${company!.id}&entityId=${id}`), enabled: !!company });
  const refresh = () => { void queryClient.invalidateQueries({ queryKey: key }); void queryClient.invalidateQueries({ queryKey: ["history", id] }); };
  const add = useMutation({
    mutationFn: (file: File) => {
      const form = new FormData();
      form.append("companyId", company!.id);
      form.append("entityId", id);
      form.append("file", file);
      return api<AttachmentDto>("/api/attachments", { method: "POST", body: form });
    },
    onSettled: () => { if (input.current) input.current.value = ""; },
    onSuccess: refresh,
  });
  const remove = useMutation({ mutationFn: (fileId: string) => api<AttachmentDto>(`/api/attachments/${fileId}`, { method: "DELETE" }), onSuccess: refresh });
  const manage = can(me, "core.attachments.manage");
  if (files.isPending) return <p className="muted">{t("common.loading")}</p>;
  if (files.isError) return <p className="error">{files.error.message}</p>;
  const when = (iso: string) => new Date(iso).toLocaleDateString(i18n.language === "am" ? "am-ET" : "en-GB", { dateStyle: "medium" });

  return (
    <>
      {files.data.length > 0 ? (
        <table className="table responsive">
          <thead><tr><th>{t("attachments.file")}</th><th className="num">{t("attachments.size")}</th><th>{t("attachments.added")}</th>{manage && <th />}</tr></thead>
          <tbody>
            {files.data.map((f) => (
              <tr key={f.id}>
                <td data-label={t("attachments.file")}><button type="button" className="link-button" onClick={() => void download(`/api/attachments/${f.id}/content`, f.fileName)}>{f.fileName}</button></td>
                <td data-label={t("attachments.size")} className="num">{size(f.size)}</td>
                <td data-label={t("attachments.added")} className="small">{when(f.uploadedAt)}{f.uploadedBy && ` · ${f.uploadedBy}`}</td>
                {manage && <td className="num"><button type="button" className="link-button" disabled={remove.isPending}
                  onClick={() => { if (window.confirm(t("attachments.confirmRemove", { name: f.fileName }))) remove.mutate(f.id); }}>{t("ui.remove")}</button></td>}
              </tr>
            ))}
          </tbody>
        </table>
      ) : <div className="empty">{t("attachments.none")}</div>}
      {manage && (
        <div className="inline-form">
          <label className="button secondary file-button">{add.isPending ? t("common.loading") : t("attachments.add")}
            <input ref={input} type="file" hidden accept=".pdf,.png,.jpg,.jpeg,.webp,.xlsx,.docx,.csv,.txt" disabled={add.isPending}
              onChange={(e) => { const file = e.target.files?.[0]; if (file) add.mutate(file); }} />
          </label>
          <span className="muted small">{t("attachments.hint")}</span>
        </div>
      )}
      {(add.isError || remove.isError) && <p className="error">{(add.error ?? remove.error)?.message}</p>}
    </>
  );
}

/** The Attachments section for an object page, when the user may see attachments; spread it before the History section. */
export function attachmentsSection(t: TFunction, me: MeDto, id: string | undefined): Section[] {
  return id && can(me, "core.attachments.read") ? [{ id: "attachments", title: t("attachments.title"), content: <Attachments id={id} /> }] : [];
}
