export function Brand({ light = false }: { light?: boolean }) {
  return (
    <div className={`brand ${light ? 'brand-light' : ''}`}>
      <span className="brand-symbol" aria-hidden="true" />
      <div>
        DocPrep<span className="brand-dot">.</span>
        <small>Panel personelu</small>
      </div>
    </div>
  )
}
