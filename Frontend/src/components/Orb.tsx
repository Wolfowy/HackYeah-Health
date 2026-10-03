import { useEffect, useId, useRef } from 'react'
import type { VoiceState } from '../models'

function wavePath(phase: number, layer: number, energy: number) {
  const points = 80
  const offset = layer * 1.37
  return (
    Array.from({ length: points + 1 }, (_, index) => {
      const angle = (index / points) * Math.PI * 2
      const ripple =
        Math.sin(angle * 3 + phase + offset) * (6 + energy * 7) +
        Math.cos(angle * 2 - phase * 0.7 + offset) * 5
      const radius = 88 + layer * 2 + ripple
      const x = 140 + Math.cos(angle) * radius * (1 + Math.sin(phase + offset) * 0.055)
      const y = 140 + Math.sin(angle) * radius * (1 + Math.cos(phase * 0.8 + offset) * 0.055)
      return `${index ? 'L' : 'M'}${x.toFixed(2)},${y.toFixed(2)}`
    }).join(' ') + 'Z'
  )
}

/** Hollow ribbons: the center has no fill. Real audio levels deform the waves. */
export function Orb({
  state = 'idle',
  small = false,
  level = 0,
}: {
  state?: VoiceState
  small?: boolean
  level?: number
}) {
  const id = useId().replace(/:/g, '')
  const paths = useRef<(SVGPathElement | null)[]>([])
  const activity = useRef({ state, level })
  activity.current = { state, level }
  useEffect(() => {
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)')
    let frame = 0
    let previous = 0
    let phase = 0
    function animate(time: number) {
      if (time - previous >= 32) {
        const { state: currentState, level: currentLevel } = activity.current
        const delta = previous ? Math.min(time - previous, 64) / 1000 : 0
        previous = time
        if (currentState !== 'paused' && !reduceMotion.matches) {
          phase +=
            delta * (currentState === 'speaking' ? 1.25 : currentState === 'listening' ? 0.8 : 0.35)
        }
        const energy = reduceMotion.matches ? 0 : Math.min(1, Math.max(0, currentLevel))
        paths.current.forEach((path, layer) => {
          path?.setAttribute('d', wavePath(phase, layer, energy))
          path?.setAttribute(
            'opacity',
            String(0.32 + Math.sin(phase * 0.9 + layer) * 0.13 + energy * 0.14),
          )
        })
      }
      frame = requestAnimationFrame(animate)
    }
    frame = requestAnimationFrame(animate)
    return () => cancelAnimationFrame(frame)
  }, [])

  return (
    <div className={`orb-scene ${small ? 'orb-small' : ''}`} data-state={state} aria-hidden="true">
      <svg className="orb-waves" viewBox="0 0 280 280" fill="none">
        <defs>
          {Array.from({ length: 5 }, (_, layer) => (
            <linearGradient
              key={layer}
              id={`${id}-wave-${layer}`}
              x1="45"
              y1="55"
              x2="240"
              y2="220"
              gradientUnits="userSpaceOnUse"
              gradientTransform={`rotate(${layer * 65} 140 140)`}
            >
              <stop className="wave-cyan" stopColor="#31c8ec" />
              <stop className="wave-blue" offset=".35" stopColor="#5083f7" />
              <stop className="wave-purple" offset=".68" stopColor="#9360ee" />
              <stop className="wave-pink" offset="1" stopColor="#ef9bde" />
            </linearGradient>
          ))}
        </defs>
        {Array.from({ length: 5 }, (_, layer) => (
          <path
            key={layer}
            ref={(node) => {
              paths.current[layer] = node
            }}
            className={`orb-wave orb-wave-${layer}`}
            d={wavePath(0, layer, 0)}
            stroke={`url(#${id}-wave-${layer})`}
            strokeWidth={layer % 2 ? 16 : 12}
            strokeLinecap="round"
            opacity=".4"
          />
        ))}
      </svg>
    </div>
  )
}
