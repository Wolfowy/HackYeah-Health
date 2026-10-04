/** Quiet ringback, unlocked by a user gesture and independent of microphone audio. */
export class ConnectionTone {
  private context: AudioContext | null = null
  private timer: ReturnType<typeof setInterval> | null = null
  private active = new Set<{ gain: GainNode; oscillators: OscillatorNode[] }>()

  prepare() {
    try {
      if (!this.context) this.context = new AudioContext()
      // Call synchronously from the click handler, before network/permission awaits.
      void this.context.resume().catch(() => {})
    } catch {
      // Unsupported/blocked audio must not prevent the conversation from starting.
    }
  }

  start() {
    if (!this.context || this.timer !== null) return
    const context = this.context
    const pulse = () => {
      const gain = context.createGain()
      const oscillators = [440, 480].map((frequency) => {
        const oscillator = context.createOscillator()
        oscillator.frequency.value = frequency
        oscillator.connect(gain)
        return oscillator
      })
      const voice = { gain, oscillators }
      this.active.add(voice)
      gain.connect(context.destination)
      const now = context.currentTime
      gain.gain.setValueAtTime(0, now)
      gain.gain.linearRampToValueAtTime(0.018, now + 0.04)
      gain.gain.setValueAtTime(0.018, now + 0.45)
      gain.gain.linearRampToValueAtTime(0, now + 0.6)
      oscillators[0].onended = () => {
        oscillators.forEach((oscillator) => oscillator.disconnect())
        gain.disconnect()
        this.active.delete(voice)
      }
      oscillators.forEach((oscillator) => {
        oscillator.start(now)
        oscillator.stop(now + 0.62)
      })
    }
    try {
      pulse()
      this.timer = setInterval(pulse, 2400)
    } catch {
      this.stop()
    }
  }

  stop() {
    if (this.timer !== null) clearInterval(this.timer)
    this.timer = null
    for (const { gain, oscillators } of this.active) {
      oscillators.forEach((oscillator) => {
        oscillator.onended = null
        try {
          oscillator.stop()
        } catch {
          /* Already ended. */
        }
        oscillator.disconnect()
      })
      gain.disconnect()
    }
    this.active.clear()
  }

  dispose() {
    this.stop()
    const context = this.context
    this.context = null
    if (context && context.state !== 'closed') void context.close().catch(() => {})
  }
}
