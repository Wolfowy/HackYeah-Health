import assert from 'node:assert/strict'
import { test } from 'node:test'
import { displayAgentText } from './agent-text'

test('removes recognised English and Polish delivery tags, preserving punctuation and paragraphs', () => {
  assert.equal(
    displayAgentText('[sighs] Rozumiem. [whispers] Od kiedy boli?'),
    'Rozumiem. Od kiedy boli?',
  )
  assert.equal(
    displayAgentText('[ ŚMIEJE SIĘ ] Rozumiem [wzdycha], dziękuję.\n[szeptem] Kontynuuj.'),
    'Rozumiem, dziękuję.\nKontynuuj.',
  )
})

test('preserves doses, medical annotations and unknown bracketed content', () => {
  const text = 'Paracetamol [500 mg], [ból po prawej stronie], [zalecenie lekarza].'
  assert.equal(displayAgentText(text), text)
  assert.equal(displayAgentText('[laughs] Dawka [500 mg].'), 'Dawka [500 mg].')
})

test('handles successive streamed updates and tag-only messages without dropping incomplete doses', () => {
  assert.equal(displayAgentText('Rozumiem. [whis'), 'Rozumiem.')
  assert.equal(displayAgentText('Rozumiem. [whispers] Od kiedy boli?'), 'Rozumiem. Od kiedy boli?')
  assert.equal(displayAgentText('[sighs] [laughs]'), '')
  assert.equal(displayAgentText('Dawka [500'), 'Dawka [500')
})
