/**
 * Page building blocks after the SAP Fiori floorplans (layout and interaction only; Gebeta's own colors, type and icons):
 * dynamic page with a header that shrinks on scroll, list report (filter bar + table toolbar), object page
 * (header with key figures, anchor bar, sections), object status, message strip, dialog and footer bar.
 */
import { useEffect, useRef, useState, type ReactNode } from "react";

export type Tone = "positive" | "critical" | "negative" | "information" | "neutral";

/** A status as coloured text (Fiori "object status"). */
export function ObjectStatus({ tone = "neutral", children }: { tone?: Tone; children: ReactNode }) {
  return <span className={`object-status ${tone}`}>{children}</span>;
}

export const statusTone = (status: string): Tone =>
  ({ Submitted: "positive", Closed: "positive", Active: "positive", Draft: "critical", Reopened: "critical", Cancelled: "negative", Left: "negative" } as Record<string, Tone>)[status] ?? "neutral";

export function MessageStrip({ tone = "information", children, onClose }: { tone?: Tone; children: ReactNode; onClose?: () => void }) {
  return (
    <div className={`message-strip ${tone}`} role={tone === "negative" ? "alert" : "status"}>
      <span>{children}</span>
      {onClose && <button className="link-button" aria-label="Close" onClick={onClose}>×</button>}
    </div>
  );
}

/** Shrinks the page header once the content scrolls under it. */
function useScrolled() {
  const [scrolled, setScrolled] = useState(false);
  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 40);
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => window.removeEventListener("scroll", onScroll);
  }, []);
  return scrolled;
}

export type Kpi = { label: string; value: ReactNode; tone?: Tone };

function KpiRow({ kpis }: { kpis: Kpi[] }) {
  return (
    <div className="kpis">
      {kpis.map((k) => (
        <div key={k.label} className="kpi">
          <div className="kpi-label">{k.label}</div>
          <div className={`kpi-value ${k.tone ?? ""}`}>{k.value}</div>
        </div>
      ))}
    </div>
  );
}

/** A full-screen page with a title area, actions at the top right, header content and the page body. */
export function DynamicPage({ title, subtitle, breadcrumb, actions, header, footer, children }: {
  title: ReactNode; subtitle?: ReactNode; breadcrumb?: ReactNode; actions?: ReactNode; header?: ReactNode; footer?: ReactNode; children: ReactNode;
}) {
  const scrolled = useScrolled();
  const page = useRef<HTMLDivElement>(null);
  const head = useRef<HTMLElement>(null);
  useEffect(() => {
    // the anchor bar sticks right under the header, whatever its height
    if (!head.current || typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver(([entry]) => page.current?.style.setProperty("--header-h", `${Math.round(entry!.borderBoxSize[0]!.blockSize)}px`));
    observer.observe(head.current);
    return () => observer.disconnect();
  }, []);
  return (
    <div ref={page} className={`dynamic-page ${footer ? "has-footer" : ""}`}>
      <header ref={head} className={`page-header ${scrolled ? "snapped" : ""}`}>
        {breadcrumb && <div className="breadcrumb">{breadcrumb}</div>}
        <div className="page-title-row">
          <div className="page-title">
            <h1>{title}</h1>
            {subtitle && <div className="page-subtitle">{subtitle}</div>}
          </div>
          {actions && <div className="page-actions">{actions}</div>}
        </div>
        {header && <div className="page-header-content">{header}</div>}
      </header>
      <div className="page-body">{children}</div>
      {footer && <footer className="footer-bar">{footer}</footer>}
    </div>
  );
}

/** Filter fields with their labels above, as in the Fiori filter bar. */
export function FilterBar({ children }: { children: ReactNode }) {
  return <div className="filter-bar">{children}</div>;
}

export function FilterField({ label, children }: { label: string; children: ReactNode }) {
  return <label className="filter-field"><span>{label}</span>{children}</label>;
}

/** List report: filters in the header, a table card with its own toolbar (title with count, actions). */
export function ListReport({ title, filters, tableTitle, count, toolbar, actions, messages, children }: {
  title: ReactNode; filters?: ReactNode; tableTitle: string; count?: number; toolbar?: ReactNode; actions?: ReactNode; messages?: ReactNode; children: ReactNode;
}) {
  return (
    <DynamicPage title={title} actions={actions} header={filters}>
      {messages}
      <section className="table-card">
        <div className="table-toolbar">
          <h2>{tableTitle}{count !== undefined && <span className="count"> ({count})</span>}</h2>
          <div className="toolbar-actions">{toolbar}</div>
        </div>
        {children}
      </section>
    </DynamicPage>
  );
}

export type Section = { id: string; title: string; content: ReactNode; hidden?: boolean };

/** Object page: header with key figures, an anchor bar that jumps to each section, and the sections below. */
export function ObjectPage({ title, subtitle, breadcrumb, status, kpis, filters, actions, messages, sections, footer }: {
  title: ReactNode; subtitle?: ReactNode; breadcrumb?: ReactNode; status?: ReactNode; kpis?: Kpi[]; filters?: ReactNode; actions?: ReactNode; messages?: ReactNode;
  sections: Section[]; footer?: ReactNode;
}) {
  const visible = sections.filter((s) => !s.hidden);
  const [active, setActive] = useState(visible[0]?.id);
  const refs = useRef<Record<string, HTMLElement | null>>({});
  useEffect(() => {
    const observer = new IntersectionObserver(
      (entries) => {
        const top = entries.filter((e) => e.isIntersecting).sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top)[0];
        if (top) setActive(top.target.id.replace(/^section-/, ""));
      },
      { rootMargin: "-140px 0px -60% 0px" },
    );
    Object.values(refs.current).forEach((el) => el && observer.observe(el));
    return () => observer.disconnect();
  }, [visible.length]);
  const jump = (id: string) => {
    setActive(id);
    refs.current[id]?.scrollIntoView({ behavior: "smooth", block: "start" });
  };

  return (
    <DynamicPage
      title={title} breadcrumb={breadcrumb} actions={actions} footer={footer}
      subtitle={<>{subtitle}{status && <span className="title-status">{status}</span>}</>}
      header={filters || kpis?.length ? <>{filters}{kpis?.length ? <div className={filters ? "kpis-gap" : ""}><KpiRow kpis={kpis} /></div> : null}</> : undefined}
    >
      <nav className="anchor-bar" aria-label="Sections">
        {visible.map((s) => (
          <button key={s.id} className={active === s.id ? "on" : ""} onClick={() => jump(s.id)}>{s.title}</button>
        ))}
      </nav>
      {messages}
      {visible.map((s) => (
        <section key={s.id} id={`section-${s.id}`} ref={(el) => { refs.current[s.id] = el; }} className="object-section">
          <h2>{s.title}</h2>
          {s.content}
        </section>
      ))}
    </DynamicPage>
  );
}

/** Label/value pairs in a responsive grid (Fiori "form" in display mode). */
export function Fields({ items }: { items: { label: string; value: ReactNode; hidden?: boolean }[] }) {
  return (
    <dl className="fields">
      {items.filter((i) => !i.hidden).map((i) => (
        <div key={i.label}><dt>{i.label}</dt><dd>{i.value ?? "—"}</dd></div>
      ))}
    </dl>
  );
}

/** A modal dialog with a title, content and a footer of buttons. */
export function Dialog({ open, title, onClose, footer, children }: { open: boolean; title: string; onClose: () => void; footer?: ReactNode; children: ReactNode }) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const d = ref.current;
    if (!d) return;
    if (open && !d.open) d.showModal();
    if (!open && d.open) d.close();
  }, [open]);
  return (
    <dialog ref={ref} className="fiori-dialog" onClose={onClose} onCancel={onClose}>
      <header>{title}</header>
      <div className="dialog-body">{children}</div>
      {footer && <footer>{footer}</footer>}
    </dialog>
  );
}

/** A launchpad tile: title, subtitle, a key figure and a footer line; the whole tile is a link. */
export function Tile({ title, subtitle, value, unit, footer, tone, onOpen }: {
  title: string; subtitle?: string; value?: ReactNode; unit?: string; footer?: string; tone?: Tone; onOpen: () => void;
}) {
  return (
    <button className="tile" onClick={onOpen}>
      <div className="tile-title">{title}</div>
      {subtitle && <div className="tile-subtitle">{subtitle}</div>}
      <div className="tile-kpi">
        {value !== undefined && <span className={`tile-value ${tone ?? ""}`}>{value}</span>}
        {unit && <span className="tile-unit">{unit}</span>}
      </div>
      {footer && <div className="tile-footer">{footer}</div>}
    </button>
  );
}
