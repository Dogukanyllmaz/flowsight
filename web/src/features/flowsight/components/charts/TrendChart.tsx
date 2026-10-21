import { useState, type KeyboardEvent, type PointerEvent } from 'react'
import { niceMax } from './BarChart'
import { useStrings } from '../../i18n/I18n'

export interface TrendSeries {
  key: string
  label: string
  /** Index into the chart's series colours (1-based, fixed order). */
  slot: 1 | 2
  values: (number | null)[]
}

const width = 700
const height = 260
const margin = { top: 16, right: 56, bottom: 32, left: 36 }

/** One-axis line chart with a crosshair readout (pointer and arrow keys), direct end labels and a hidden table. */
export function TrendChart({ label, summary, xLabels, series, format, columns, marker }: {
  label: string
  summary: string
  xLabels: string[]
  series: TrendSeries[]
  format: (value: number | null) => string
  /** Header label for the hidden table's first column. */
  columns: string
  /** Optional labelled vertical rule, e.g. the month a process change took effect. */
  marker?: { index: number; label: string }
}) {
  const [active, setActive] = useState<number | null>(null)
  const { language } = useStrings()
  const tickFormat = new Intl.NumberFormat(language === 'tr' ? 'tr-TR' : 'en-GB', { maximumFractionDigits: 2 })
  const all = series.flatMap((s) => s.values).filter((v): v is number => v !== null)
  const max = niceMax(Math.max(1, ...all))
  const plotW = width - margin.left - margin.right
  const plotH = height - margin.top - margin.bottom
  const x = (i: number) => margin.left + (xLabels.length <= 1 ? plotW / 2 : (i / (xLabels.length - 1)) * plotW)
  const y = (v: number) => margin.top + plotH - (v / max) * plotH
  const ticks = [0, max / 4, max / 2, (3 * max) / 4, max]

  const path = (values: (number | null)[]) =>
    values
      .map((v, i) => (v === null ? null : `${x(i)},${y(v)}`))
      .reduce<string[]>((parts, point, i) => {
        if (point === null) return parts
        parts.push(`${i === 0 || values[i - 1] === null ? 'M' : 'L'}${point}`)
        return parts
      }, [])
      .join(' ')

  const onPointer = (event: PointerEvent<SVGRectElement>) => {
    const box = event.currentTarget.ownerSVGElement!.getBoundingClientRect()
    const px = ((event.clientX - box.left) / box.width) * width
    const step = xLabels.length <= 1 ? 1 : plotW / (xLabels.length - 1)
    setActive(Math.max(0, Math.min(xLabels.length - 1, Math.round((px - margin.left) / step))))
  }

  const onKey = (event: KeyboardEvent<SVGRectElement>) => {
    if (event.key === 'ArrowRight') setActive((a) => Math.min(xLabels.length - 1, (a ?? -1) + 1))
    if (event.key === 'ArrowLeft') setActive((a) => Math.max(0, (a ?? xLabels.length) - 1))
  }

  const lastIndex = xLabels.length - 1

  return (
    <figure className="fs-chart">
      <figcaption className="fs-chart-title">{label}</figcaption>
      <div className="fs-legend" aria-hidden="true">
        {series.map((s) => (
          <span key={s.key}>
            <i className={`fs-swatch fs-series-${s.slot}`} />
            {s.label}
          </span>
        ))}
      </div>
      <div className="fs-chart-frame">
        <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label={`${label}. ${summary}`}>
          {ticks.map((tick) => (
            <g key={tick} className="fs-grid">
              <line x1={margin.left} x2={width - margin.right} y1={y(tick)} y2={y(tick)} />
              <text x={margin.left - 6} y={y(tick)} dy="0.32em" textAnchor="end">
                {tickFormat.format(tick)}
              </text>
            </g>
          ))}
          {xLabels.map((l, i) =>
            i % 2 === lastIndex % 2 ? (
              <text key={l} className="fs-axis-label" x={x(i)} y={height - 10} textAnchor="middle">
                {l}
              </text>
            ) : null,
          )}
          <line className="fs-axis" x1={margin.left} x2={width - margin.right} y1={y(0)} y2={y(0)} />
          {marker && marker.index >= 0 && marker.index < xLabels.length && (
            <g className="fs-marker">
              <line x1={x(marker.index)} x2={x(marker.index)} y1={margin.top} y2={margin.top + plotH} />
              <text x={x(marker.index) + 4} y={margin.top + 10}>
                {marker.label}
              </text>
            </g>
          )}
          {series.map((s) => {
            const lastValue = s.values[lastIndex]
            return (
              <g key={s.key} className={`fs-line fs-series-${s.slot}`}>
                <path d={path(s.values)} />
                {lastValue !== null && lastValue !== undefined && (
                  <text className="fs-line-label" x={x(lastIndex) + 6} y={y(lastValue)} dy="0.32em">
                    {s.label}
                  </text>
                )}
              </g>
            )
          })}
          {active !== null && (
            <g className="fs-crosshair">
              <line x1={x(active)} x2={x(active)} y1={margin.top} y2={margin.top + plotH} />
              {series.map((s) =>
                s.values[active] === null ? null : (
                  <circle key={s.key} className={`fs-series-${s.slot}`} cx={x(active)} cy={y(s.values[active]!)} r={4} />
                ),
              )}
            </g>
          )}
          <rect
            x={margin.left}
            y={margin.top}
            width={plotW}
            height={plotH}
            fill="transparent"
            tabIndex={0}
            aria-label={label}
            onPointerMove={onPointer}
            onPointerLeave={() => setActive(null)}
            onFocus={() => setActive(lastIndex)}
            onBlur={() => setActive(null)}
            onKeyDown={onKey}
          />
        </svg>
        {active !== null && (
          <div className="fs-tooltip" style={{ left: `${(x(active) / width) * 100}%` }} role="status">
            <div className="fs-tooltip-title">{xLabels[active]}</div>
            {series.map((s) => (
              <div key={s.key}>
                <strong>{format(s.values[active])}</strong> <span className="fs-muted">{s.label}</span>
              </div>
            ))}
          </div>
        )}
      </div>
      <table className="fs-sr-only">
        <caption>{label}</caption>
        <thead>
          <tr>
            <th scope="col">{columns}</th>
            {series.map((s) => (
              <th key={s.key} scope="col">
                {s.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {xLabels.map((l, i) => (
            <tr key={l}>
              <th scope="row">{l}</th>
              {series.map((s) => (
                <td key={s.key}>{format(s.values[i])}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </figure>
  )
}
