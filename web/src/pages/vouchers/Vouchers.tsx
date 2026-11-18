import type { ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "@tanstack/react-router";
import { useTranslation } from "react-i18next";
import { useSession } from "../../app/context";
import { api } from "../../lib/api";
import { formatDate, formatMoney } from "../../lib/ethiopian";
import type { CashierDto, CashLedgerEntryDto, JournalEntryDto, PartyDto, PaymentDto } from "../../lib/types";
import { MessageStrip } from "../../ui/fiori";

type Words = { english: string; amharic: string };

export type VoucherLine = { label: string; account?: string; debit?: number; credit?: number; amount?: number };

export type VoucherModel = {
  title: string;
  number?: string;
  date: string;
  status: string;
  party?: { label: string; name: string; tin?: string };
  via?: { label: string; value: string };
  lines: VoucherLine[];
  total: number;
  remark?: string;
  journal?: boolean;
};

/**
 * A payment, receipt or journal voucher laid out to print and sign: company, Ethiopian date (with the Gregorian date in brackets),
 * party, lines, the total in figures and in words in Amharic and English (both, whatever the screen language), and four signature
 * boxes. Drafts and cancelled documents print with a watermark. The menu and top bar are hidden when printing.
 */
export function Voucher({ model, back }: { model: VoucherModel; back: ReactNode }) {
  const { t, i18n } = useTranslation();
  const { me, company } = useSession();
  const total = Math.abs(model.total); // a reversal says so in its title; the words never say "minus"
  const words = useQuery({ queryKey: ["amount-in-words", total], queryFn: () => api<Words>(`/api/core/amount-in-words?amount=${total}`) });
  const watermark = model.status === "Draft" ? t("voucher.draft") : model.status === "Cancelled" ? t("voucher.cancelled") : null;

  return (
    <>
      <div className="no-print inline-fields">
        {back}
        <button onClick={() => window.print()}>{t("me.print")}</button>
      </div>
      <article className="receipt voucher">
        {watermark && <div className="watermark" aria-hidden>{watermark}</div>}
        <header>
          <div><strong>{company?.name}</strong></div>
          <div className="muted small">{t("fields.tin")} <span className="mono">{company?.tin}</span></div>
          <h2>{model.title}</h2>
          <div className="mono">{model.number ?? "—"}</div>
          <div>{formatDate(model.date, "Both", i18n.language === "am")}</div>
        </header>
        {(model.party || model.via) && (
          <section className="parties">
            {model.party && <div><div className="card-label">{model.party.label}</div><strong>{model.party.name}</strong>
              {model.party.tin && <div>{t("fields.tin")} <span className="mono">{model.party.tin}</span></div>}</div>}
            {model.via && <div><div className="card-label">{model.via.label}</div><strong className="mono">{model.via.value}</strong></div>}
          </section>
        )}
        <table className="table">
          <thead>
            {model.journal
              ? <tr><th>{t("acc.code")}</th><th>{t("cash.description")}</th><th className="num">{t("acc.debit")}</th><th className="num">{t("acc.credit")}</th></tr>
              : <tr><th>{t("cash.description")}</th><th className="num">{t("cash.amount")}</th></tr>}
          </thead>
          <tbody>
            {model.lines.map((l, i) => model.journal
              ? <tr key={i}><td className="mono">{l.account}</td><td>{l.label}</td><td className="num">{l.debit ? formatMoney(l.debit) : ""}</td><td className="num">{l.credit ? formatMoney(l.credit) : ""}</td></tr>
              : <tr key={i}><td>{l.label}</td><td className="num">{formatMoney(l.amount ?? 0)}</td></tr>)}
          </tbody>
          <tfoot>
            {model.journal
              ? <tr className="report-total"><td colSpan={2}>{t("voucher.total")}</td><td className="num">{formatMoney(total)}</td><td className="num">{formatMoney(total)}</td></tr>
              : <tr className="report-total"><td>{t("voucher.total")}</td><td className="num">{formatMoney(total)}</td></tr>}
          </tfoot>
        </table>
        <div className="in-words">
          <div><span className="card-label">{t("voucher.inWords")}</span> {words.data?.amharic ?? "…"}</div>
          <div>{words.data?.english ?? "…"}</div>
        </div>
        {model.remark && <p className="small">{t("acc.remark")}: {model.remark}</p>}
        <footer className="signatures four">
          {(["prepared", "checked", "approved", "received"] as const).map((s) => <div key={s}>{t(`voucher.${s}`)}<span /></div>)}
        </footer>
      </article>
      <p className="no-print muted small">{t("receipt.printedFor", { user: me.fullName ?? me.username })}</p>
    </>
  );
}

function Loading({ error }: { error?: Error | null }) {
  const { t } = useTranslation();
  return error ? <MessageStrip tone="negative">{error.message}</MessageStrip> : <p className="muted">{t("common.loading")}</p>;
}

/** A journal voucher (JV-): every line with its account, debit and credit. */
export function JournalVoucher() {
  const { t } = useTranslation();
  const { id } = useParams({ from: "/accounting/journals/$id/voucher" });
  const journal = useQuery({ queryKey: ["journal", id], queryFn: () => api<JournalEntryDto>(`/api/accounting/journals/${id}`) });
  if (!journal.data) return <Loading error={journal.error} />;
  const j = journal.data;
  return (
    <Voucher back={<Link to="/accounting/journals/$id" params={{ id }} className="back">← {j.number}</Link>} model={{
      title: t("voucher.journal"), number: j.number, date: j.postingDate, status: j.status, journal: true, remark: j.remark, total: j.totalDebit,
      lines: j.lines.map((l) => ({ account: l.accountCode, label: [l.accountName, l.description].filter(Boolean).join(" · "), debit: l.debit, credit: l.credit })),
    }} />
  );
}

/** A payment (PAY-) as a payment or receipt voucher: the party, the bank account, and the invoices it settles. */
export function PaymentVoucher() {
  const { t } = useTranslation();
  const { id } = useParams({ from: "/accounting/payments/$id/voucher" });
  const payment = useQuery({ queryKey: ["payment", id], queryFn: () => api<PaymentDto>(`/api/accounting/payments/${id}`) });
  const party = useQuery({ queryKey: ["party", payment.data?.partyId], queryFn: () => api<PartyDto>(`/api/core/parties/${payment.data!.partyId}`), enabled: !!payment.data });
  if (!payment.data) return <Loading error={payment.error} />;
  const p = payment.data;
  const receive = p.kind === "Receive";
  const settled = p.allocations?.length ? p.allocations : [{ invoiceId: p.invoiceId, invoiceNumber: p.invoiceNumber, amount: p.amount }];
  return (
    <Voucher back={<Link to="/accounting/invoices/$id" params={{ id: p.invoiceId }} className="back">← {p.invoiceNumber}</Link>} model={{
      title: t(receive ? "voucher.receipt" : "voucher.payment"), number: p.number, date: p.postingDate, status: p.status, total: p.amount, remark: p.reference,
      party: { label: t(receive ? "voucher.receivedFrom" : "voucher.paidTo"), name: p.partyName, tin: party.data?.tin },
      via: { label: t("voucher.bankAccount"), value: p.bankAccountCode },
      lines: settled.map((a) => ({ label: `${t("voucher.invoice")} ${a.invoiceNumber ?? ""}`, amount: a.amount })),
    }} />
  );
}

/** A cashier receipt or payment (CR-, CP-, transfers, petty cash top-ups) from the cashier's ledger, with the movement's own number and date. */
export function CashVoucher() {
  const { t } = useTranslation();
  const { company } = useSession();
  const { id, number } = useParams({ from: "/cash/cashiers/$id/voucher/$number" });
  const ledger = useQuery({ queryKey: ["cash-ledger", id], queryFn: () => api<CashLedgerEntryDto[]>(`/api/cash/cashiers/${id}/ledger`) });
  const cashiers = useQuery({ queryKey: ["cashiers", company?.id], queryFn: () => api<CashierDto[]>(`/api/cash/cashiers?companyId=${company!.id}`), enabled: !!company });
  if (!ledger.data) return <Loading error={ledger.error} />;
  const entry = ledger.data.find((e) => e.sourceNumber === number);
  if (!entry) return <MessageStrip tone="negative">{t("voucher.notFound")}</MessageStrip>;
  const incoming = entry.amount > 0;
  return (
    <Voucher back={<Link to="/cash/cashiers/$id" params={{ id }} className="back">← {t("nav.cashiers")}</Link>} model={{
      title: t(incoming ? "voucher.cashReceipt" : "voucher.cashPayment"), number: entry.sourceNumber, date: entry.date, status: "Submitted", total: entry.amount,
      via: { label: t("voucher.cashier"), value: cashiers.data?.find((c) => c.id === id)?.name ?? "" },
      lines: [{ label: `${entry.kind} · ${entry.description}`, amount: Math.abs(entry.amount) }],
    }} />
  );
}
