import { useState } from 'react';
import { toErrorMessage } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import { Field } from '../../../components/Field';
import type { TicketDetail, TicketEscalation } from '../../../types/api';
import { canEscalate } from '../canEscalate';

interface EscalateTicketControlProps {
  ticket: TicketDetail;
  onEscalated: (ticket: TicketDetail, escalation: TicketEscalation) => void;
}

interface FormState {
  reason: string;
  escalatedBy: string;
}

const emptyForm: FormState = {
  reason: '',
  escalatedBy: '',
};

/**
 * Escalates a ticket by one priority level. Client validation matches the API
 * (reason 5–500, actor required) so invalid input never leaves the browser.
 */
export function EscalateTicketControl({ ticket, onEscalated }: EscalateTicketControlProps) {
  const [form, setForm] = useState<FormState>(emptyForm);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [isSaving, setIsSaving] = useState(false);
  const [submitError, setSubmitError] = useState<string>();

  const allowed = canEscalate(ticket);

  const update = (patch: Partial<FormState>) => setForm((current) => ({ ...current, ...patch }));

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const validationErrors = validate(form);
    setErrors(validationErrors);
    setSubmitError(undefined);

    if (Object.keys(validationErrors).length > 0) {
      return;
    }

    setIsSaving(true);

    try {
      const result = await ticketsApi.escalate(ticket.id, {
        reason: form.reason.trim(),
        escalatedBy: form.escalatedBy.trim(),
      });

      setForm(emptyForm);
      onEscalated(result.ticket, result.escalation);
    } catch (caught) {
      setSubmitError(toErrorMessage(caught, 'Could not escalate the ticket.'));
    } finally {
      setIsSaving(false);
    }
  };

  if (!allowed) {
    return (
      <div className="card">
        <h2>Escalate</h2>
        <p className="field__hint">
          {ticket.priority === 'Critical'
            ? 'Critical tickets cannot be escalated further.'
            : `${ticket.status} tickets cannot be escalated.`}
        </p>
      </div>
    );
  }

  return (
    <form className="card form" onSubmit={(event) => void submit(event)} noValidate>
      <h2>Escalate</h2>
      <p className="field__hint">
        Raises priority one level, recomputes the SLA due date, and re-evaluates ownership.
      </p>

      <Field
        id="escalate-reason"
        label="Reason"
        error={errors.reason}
        hint="Between 5 and 500 characters."
      >
        {(fieldProps) => (
          <textarea
            {...fieldProps}
            rows={3}
            value={form.reason}
            maxLength={500}
            disabled={isSaving}
            onChange={(event) => update({ reason: event.target.value })}
          />
        )}
      </Field>

      <Field
        id="escalate-by"
        label="Escalated by"
        error={errors.escalatedBy}
        hint="Your name or email until sign-in is available."
      >
        {(fieldProps) => (
          <input
            {...fieldProps}
            type="text"
            value={form.escalatedBy}
            maxLength={100}
            disabled={isSaving}
            onChange={(event) => update({ escalatedBy: event.target.value })}
          />
        )}
      </Field>

      {submitError && (
        <p className="field__error" role="alert">
          {submitError}
        </p>
      )}

      <div className="button-row">
        <button type="submit" className="button button--primary" disabled={isSaving}>
          {isSaving ? 'Escalating...' : 'Escalate ticket'}
        </button>
      </div>
    </form>
  );
}

export function validateEscalateForm(form: FormState): Record<string, string> {
  return validate(form);
}

function validate(form: FormState): Record<string, string> {
  const errors: Record<string, string> = {};
  const reason = form.reason.trim();
  const escalatedBy = form.escalatedBy.trim();

  if (reason.length < 5) {
    errors.reason = 'Give a reason of at least 5 characters.';
  } else if (reason.length > 500) {
    errors.reason = 'Keep the reason to 500 characters or fewer.';
  }

  if (escalatedBy.length === 0) {
    errors.escalatedBy = 'Say who is escalating the ticket.';
  } else if (escalatedBy.length > 100) {
    errors.escalatedBy = 'Keep the escalated-by value to 100 characters or fewer.';
  }

  return errors;
}
