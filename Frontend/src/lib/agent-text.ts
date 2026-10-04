// Presentation only: keep the original transcript in the conversation history.
const deliveryTags = new Set([
  'laughs',
  'laughing',
  'chuckles',
  'chuckling',
  'giggles',
  'sighs',
  'sighing',
  'whispers',
  'whispering',
  'shouts',
  'shouting',
  'crying',
  'sobbing',
  'gasps',
  'clears throat',
  'coughs',
  'inhales',
  'exhales',
  'short pause',
  'long pause',
  'pause',
  'excited',
  'happy',
  'sad',
  'angry',
  'curious',
  'sarcastic',
  'calm',
  'thoughtful',
  'empathetic',
  'serious',
  'cheerful',
  'śmiech',
  'śmieje się',
  'chichocze',
  'wzdycha',
  'westchnienie',
  'szept',
  'szeptem',
  'szepcze',
  'krzyczy',
  'płacze',
  'szlocha',
  'chrząka',
  'kaszle',
  'wdech',
  'wydech',
  'pauza',
  'krótka pauza',
  'długa pauza',
  'spokojnie',
  'radośnie',
  'ze smutkiem',
  'z ciekawością',
  'z empatią',
])

const normalizeTag = (tag: string) => tag.trim().toLocaleLowerCase('pl-PL').replace(/\s+/g, ' ')

export function displayAgentText(text: string): string {
  const withoutTags = text.replace(/\[([^\]\r\n]*)\]/g, (match, tag: string) =>
    deliveryTags.has(normalizeTag(tag)) ? ' ' : match,
  )
  // A streamed tag can arrive in pieces. Hide it only once it is recognisable;
  // incomplete doses or other bracketed medical information remain visible.
  const withoutPartialTag = withoutTags.replace(/\[([^\]\r\n]+)$/, (match, tag: string) => {
    const partial = normalizeTag(tag)
    return partial.length >= 3 && [...deliveryTags].some((known) => known.startsWith(partial))
      ? ''
      : match
  })
  return withoutPartialTag
    .replace(/[ \t]{2,}/g, ' ')
    .replace(/[ \t]+([,.;:!?])/g, '$1')
    .replace(/[ \t]*\n[ \t]*/g, '\n')
    .trim()
}
