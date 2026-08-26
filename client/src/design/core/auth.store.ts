import { Injectable, computed, signal } from '@angular/core';
import { MOCK_EVENTS, MOCK_PASSKEYS, MOCK_SESSIONS } from './mock-data';
import { CeremonyError, CeremonyState, Passkey, SecurityEvent, Session } from './models';

/* Signal store over mock data. Timers stand in for network latency so the
   ceremony states can be driven and reviewed without a server. */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  // ── ceremony ────────────────────────────────────────────────────────
  readonly ceremony = signal<CeremonyState>('idle');
  readonly ceremonyError = signal<CeremonyError | null>(null);

  /** Text handed to the aria-live region. One sentence, specific, actionable. */
  readonly liveMessage = computed(() => {
    switch (this.ceremony()) {
      case 'waiting':
        return 'Waiting for your authenticator. Touch your fingerprint sensor or tap your security key.';
      case 'verifying':
        return 'Signature received. Verifying with the server.';
      case 'unsupported':
        return 'This browser cannot create or use passkeys. Choose an alternative sign-in method.';
      case 'error':
        return this.errorMessage();
      default:
        return '';
    }
  });

  /* No message is reused across causes: a rejected credential, a dismissed
     prompt and a timeout need different next actions from the user. */
  readonly errorMessage = computed(() => {
    switch (this.ceremonyError()) {
      case 'unrecognised':
        return 'This passkey was not recognised on this device. It may belong to another account, or it was removed here after being created.';
      case 'cancelled':
        return 'You dismissed the system prompt before it finished. Press Continue with passkey to try again — nothing was sent.';
      case 'timeout':
        return 'Your authenticator did not respond in 60 seconds. If you use a security key, make sure it is seated in the port and the light is on.';
      default:
        return '';
    }
  });

  /** Only 'error' and 'unsupported' are failures; waiting/verifying are not. */
  readonly isFailure = computed(
    () => this.ceremony() === 'error' || this.ceremony() === 'unsupported'
  );

  private timers: ReturnType<typeof setTimeout>[] = [];

  authenticate(outcome: 'success' | CeremonyError = 'success'): void {
    this.clearTimers();
    this.ceremonyError.set(null);
    this.ceremony.set('waiting');
    this.timers.push(setTimeout(() => {
      if (outcome === 'success') {
        this.ceremony.set('verifying');
        this.timers.push(setTimeout(() => this.ceremony.set('idle'), 1400));
      } else {
        this.ceremonyError.set(outcome);
        this.ceremony.set('error');
      }
    }, 2200));
  }

  cancelCeremony(): void {
    this.clearTimers();
    this.ceremonyError.set('cancelled');
    this.ceremony.set('error');
  }

  reportUnsupported(): void {
    this.clearTimers();
    this.ceremony.set('unsupported');
  }

  reset(): void {
    this.clearTimers();
    this.ceremonyError.set(null);
    this.ceremony.set('idle');
  }

  private clearTimers(): void {
    this.timers.forEach(clearTimeout);
    this.timers = [];
  }

  // ── passkeys ────────────────────────────────────────────────────────
  private readonly _passkeys = signal<Passkey[]>(MOCK_PASSKEYS);
  readonly passkeys = this._passkeys.asReadonly();

  readonly syncedCount = computed(
    () => this._passkeys().filter(p => p.backup === 'synced').length
  );

  /** The single source of truth for the last-passkey warning. */
  readonly isLastPasskey = computed(() => this._passkeys().length === 1);

  removePasskey(id: string): void {
    const removed = this._passkeys().find(p => p.id === id);
    this._passkeys.update(list => list.filter(p => p.id !== id));

    const eventId = nextEventId();
    this.log({
      id: eventId,
      severity: 'notice',
      title: 'Passkey removed',
      detail: `${removed?.deviceName ?? 'A passkey'} can no longer sign in to this account.`,
      meta: [eventId, removed?.authenticator ?? 'unknown authenticator'],
      timeLabel: 'Just now',
    });
  }

  // ── sessions ────────────────────────────────────────────────────────
  private readonly _sessions = signal<Session[]>(MOCK_SESSIONS);
  readonly sessions = this._sessions.asReadonly();

  readonly otherSessionCount = computed(
    () => this._sessions().filter(s => !s.isCurrent).length
  );

  revokeSession(id: string): void {
    this._sessions.update(list => list.filter(s => s.id !== id));
  }

  revokeAllOthers(): void {
    this._sessions.update(list => list.filter(s => s.isCurrent));
  }

  // ── activity ────────────────────────────────────────────────────────
  private readonly _events = signal<SecurityEvent[]>(MOCK_EVENTS);
  readonly events = this._events.asReadonly();
  readonly criticalOnly = signal(false);

  readonly visibleEvents = computed(() =>
    this.criticalOnly() ? this._events().filter(e => e.severity === 'critical') : this._events()
  );

  private log(e: SecurityEvent): void {
    this._events.update(list => [e, ...list]);
  }
}

/* Math.random is correct here and only here: this is a display label for a
   fake audit row, not an identifier anything trusts. Real event ids are minted
   server-side. Flagged explicitly so the call does not read as a lapse. */
function nextEventId(): string {
  return 'evt_' + Math.random().toString(16).slice(2, 8);
}
