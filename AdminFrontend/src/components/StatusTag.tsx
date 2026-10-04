import { Tag } from 'antd'
import { statusConfig } from '../lib/appointments'
import type { VisitStatus } from '../models'

export function StatusTag({ status }: { status: VisitStatus }) {
  const config = statusConfig[status]
  return (
    <Tag color={config.color} className={`status-tag ${config.className}`}>
      <span className="status-dot" />
      {config.label}
    </Tag>
  )
}
