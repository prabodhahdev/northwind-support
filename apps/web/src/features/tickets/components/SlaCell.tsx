import { Badge, type BadgeTone } from '../../../components/Badge';
import { formatDateTime } from '../../../lib/format';
import type { SlaStatus } from '../../../types/api';

/** Display labels for every SLA state — text, not colour alone, conveys meaning. */
export const slaStatusLabels: Record<SlaStatus, { text: string; tone: BadgeTone }> = {
  WithinSla: { text: 'Within SLA', tone: 'info' },
  AtRisk: { text: 'At risk', tone: 'warning' },
  Breached: { text: 'Breached', tone: 'danger' },
  Met: { text: 'Met', tone: 'success' },
  NotApplicable: { text: 'No SLA', tone: 'neutral' },
};

export const slaStatusOptions: SlaStatus[] = [
  'WithinSla',
  'AtRisk',
  'Breached',
  'Met',
  'NotApplicable',
];

interface SlaCellProps {
  dueAtUtc: string | null;
  slaStatus: SlaStatus;
}

/** Reusable SLA indicator used by both the ticket list and the detail page. */
export function SlaCell({ dueAtUtc, slaStatus }: SlaCellProps) {
  const label = slaStatusLabels[slaStatus];

  return (
    <div className="sla-cell">
      <Badge tone={label.tone}>{label.text}</Badge>
      <span className="sla-cell__due">{formatDateTime(dueAtUtc)}</span>
    </div>
  );
}
