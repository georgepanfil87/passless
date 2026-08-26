import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AuthStore } from '../../core/auth.store';

@Component({
  selector: 'pl-session-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="border border-line bg-surface">
      <header class="flex flex-wrap items-center justify-between gap-4 border-b border-line-soft px-5 py-5 sm:px-6">
        <div>
          <h2 class="text-title font-semibold">Sessions</h2>
          <p class="mt-1 text-body text-muted">{{ store.sessions().length }} active devices</p>
        </div>
        <button type="button" (click)="confirmRevokeAll.set(true)"
          class="border border-line px-4 py-2.75 text-body font-semibold hover:border-ink">
          Sign out everywhere else
        </button>
      </header>

      <!-- Nothing is hidden behind a menu below md: someone hunting a
           suspicious session is usually on the phone in their hand. -->
      <ul class="list-none p-0 m-0">
        @for (s of store.sessions(); track s.id) {
          <li class="grid gap-2.5 border-b border-line-soft px-5 py-4.5 last:border-b-0 sm:px-6
                     md:grid-cols-[1.5fr_1.1fr_.9fr_auto] md:items-center md:gap-4"
              [class.bg-sunk]="s.isCurrent">
            <div>
              @if (s.isCurrent) {
                <p class="mb-1.5 flex items-center gap-1.5 font-mono text-micro tracking-meta text-accent-strong">
                  <span class="block h-1.5 w-1.5 bg-accent"></span>CURRENT SESSION
                </p>
              }
              <p class="text-body-lg font-medium">{{ s.device }} · {{ s.browser }}</p>
              @if (!s.isCurrent) {
                <p class="mt-1 font-mono text-micro uppercase tracking-meta text-faint">{{ s.method }}</p>
              }
            </div>
            <p class="text-body text-ink-2">{{ s.location }} · {{ s.ip }}</p>
            <p class="font-mono text-body text-muted">{{ s.lastActivityLabel }}</p>
            @if (s.isCurrent) {
              <span class="font-mono text-meta text-faint" aria-hidden="true">—</span>
            } @else {
              <button type="button" (click)="store.revokeSession(s.id)"
                class="mt-1 border border-line px-3.5 py-3 text-body hover:border-ink md:mt-0 md:py-2">Revoke</button>
            }
          </li>
        }
      </ul>
    </section>

    @if (confirmRevokeAll()) {
      <div role="alertdialog" aria-modal="true" aria-labelledby="pl-revoke-all"
           class="mt-6 max-w-[460px] border border-line border-t-3 border-t-warn bg-surface">
        <div class="flex flex-col gap-3.5 p-6 pb-5">
          <p class="pl-eyebrow text-warn-text">Security warning</p>
          <h3 id="pl-revoke-all" class="text-title font-semibold">
            Sign out {{ store.otherSessionCount() }} other sessions?
          </h3>
          <p class="text-body text-ink-2">
            Every device except this one loses access immediately, including any session an
            attacker may hold. Each device will need its passkey to sign back in.
          </p>
        </div>
        <div class="flex justify-end gap-2.5 border-t border-line-soft bg-sunk px-6 py-4">
          <button type="button" (click)="confirmRevokeAll.set(false)"
            class="border border-line px-4 py-2.75 text-body font-medium hover:border-ink">Cancel</button>
          <button type="button" (click)="revokeAll()"
            class="border border-warn-edge bg-warn px-4 py-2.75 text-body font-semibold text-on-warn">
            Sign out everywhere else
          </button>
        </div>
      </div>
    }
  `,
})
export class SessionListComponent {
  readonly store = inject(AuthStore);
  readonly confirmRevokeAll = signal(false);

  revokeAll(): void {
    this.store.revokeAllOthers();
    this.confirmRevokeAll.set(false);
  }
}
