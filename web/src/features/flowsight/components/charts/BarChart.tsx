export type Tone = 'unknown' | 'faster' | 'normal' | 'slower' | 'muchSlower'

export interface Bar {
  key: string
  label: string
  value: number
  tone: Tone
  /** Short text shown on hover/focus and read by screen readers. */
  detail: string
  href?: string
}

const width = 700
const height = 260
const margin = { top: 28, right: 8, bottom: 40, left: 36 }
const gap = 10

/** Toned marks carry an icon as well as a colour, so pace is never colour alone. */
export const toneIcon: Record<Tone, string> = {
  unknown: '○',
  faster: '▼',
  normal: '',
  slower: '▲',
  muchSlower: '▲▲',
}

/** Rounds an axis maximum up so that four equal steps land on round numbers. */
export function niceMax(value: number): number {
  if (value <= 0) return 1
  const magnitude = 10 ** Math.floor(Math.log10(value))
  const step = [1, 2, 2.5, 5, 10].map((m) => m * magnitude).find((s) => s * 4 >= value) ?? 10 * magnitude
  return step * 4
}

/** Vertical bars on one axis, with value labels, a 4px rounded data end and a visually hidden table. */
export function BarChart({ label, summary, bars, columns }: {
  label: string
  summary: string
  bars: Bar[]
  /** Header labels for the hidden table: [category, value, detail]. */
  columns: [string, string, string]
}) {
  const max = niceMax(Math.max(0, ...bars.map((b) => b.value)))
  const plotW = width - margin.left - margin.right
  const plotH = height - margin.top - margin.bottom
  const slot = plotW / Math.max(1, bars.length)
  const barW = Math.min(64, slot - gap)
  const y = (v: number) => margin.top + plotH - (v / max) * plotH
  const ticks = [0, max / 4, max / 2, (3 * max) / 4, max]

  return (
    <figure className="fs-chart">
      <figcaption className="fs-chart-title">{label}</figcaption>
      <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label={`${label}. ${summary}`}>
        {ticks.map((t) => (
          <g key={t} className="fs-grid">
            <line x1={margin.left} x2={width - margin.right} y1={y(t)} y2={y(t)} />
            <text x={margin.left - 6} y={y(t)} dy="0.32em" textAnchor="end">
              {Math.round(t)}
            </text>
          </g>
        ))}
        {bars.map((bar, i) => {
          const x = margin.left + i * slot + (slot - barW) / 2
          const top = y(bar.value)
          const h = margin.top + plotH - top
          const r = Math.min(4, h)
          const path =
            h <= 0
              ? ''
              : `M${x},${top + h} V${top + r} Q${x},${top} ${x + r},${top} H${x + barW - r} Q${x + barW},${top} ${x + barW},${top + r} V${top + h} Z`
          const mark = (
            <g data-stage={bar.key} data-pace={bar.tone} className={`fs-bar fs-tone-${bar.tone}`}>
              <title>{bar.detail}</title>
              {/* Hit area larger than the mark. */}
              <rect x={margin.left + i * slot} y={margin.top} width={slot} height={plotH} fill="transparent" />
              <path d={path} />
              <text className="fs-bar-value" x={x + barW / 2} y={top - 6} textAnchor="middle">
                {toneIcon[bar.tone] ? `${toneIcon[bar.tone]} ` : ''}
                {bar.value}
              </text>
              <text className="fs-bar-label" x={x + barW / 2} y={height - margin.bottom + 16} textAnchor="middle">
                {bar.label}
              </text>
            </g>
          )
          return bar.href ? (
            <a key={bar.key} href={bar.href} aria-label={bar.detail}>
              {mark}
            </a>
          ) : (
            <g key={bar.key}>{mark}</g>
          )
        })}
        <line className="fs-axis" x1={margin.left} x2={width - margin.right} y1={y(0)} y2={y(0)} />
      </svg>
      <table className="fs-sr-only">
        <caption>{label}</caption>
        <thead>
          <tr>
            {columns.map((c) => (
              <th key={c} scope="col">
                {c}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {bars.map((b) => (
            <tr key={b.key}>
              <th scope="row">{b.label}</th>
              <td>{b.value}</td>
              <td>{b.detail}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </figure>
  )
}
