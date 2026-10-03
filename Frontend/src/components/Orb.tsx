import type { CSSProperties } from 'react'
import type { VoiceState } from '../models'

/** The state controls pace and amplitude; no microphone recording in the mock. */
export function Orb({ state = 'idle', small = false }: { state?: VoiceState; small?: boolean }) {
  return (
    <div className={`orb-scene ${small ? 'orb-small' : ''}`} data-state={state} aria-hidden="true">
      <div className="orb-halo" />
      <div className="orb-body">
        <div className="orb-core" />
        {Array.from({ length: 5 }, (_, index) => (
          <div
            key={index}
            className={`orb-ribbon orb-ribbon-${index}`}
            style={{ '--ribbon-index': index } as CSSProperties}
          />
        ))}
        <div className="orb-gloss" />
      </div>
      <div className="orb-shadow" />
    </div>
  )
}
