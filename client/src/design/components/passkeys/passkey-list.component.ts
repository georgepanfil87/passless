import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AuthStore } from '../../core/auth.store';
import { Passkey } from '../../core/models';

@Component({
  selector: 'pl-passkey-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="border border-line bg-surface">
      <header class="flex flex-wrap items-center justify-between gap-4 border-b border-line-soft px-6 py-5">
        <div>
          <h2 class="text-title font-semibold">Passkeys</h2>
          <p class="mt-1 text-body text-muted">
            {{ store.passkeys().length }} registered · {{ store.syncedCount() }} synced,
            {{ store.passkeys().length - store.syncedCount() }} device-bound
          </p>
        </div>
        <button type="button" class="bg-ink px-4 py-2.75 text-body font-semibold text-surface">Add passkey</button>
      </header>

      <!-- Visual column headers only; the per-row labels below carry the same
           text to assistive tech at every breakpoint. -->
      <div class="hidden grid-cols-[minmax(200px,1.4fr)_1fr_.8fr_.8fr_auto] gap-4 border-b border-line-soft
                  px-6 py-2.5 md:grid" aria-hidden="true">
        <span class="pl-eyebrow">Device</span><span class="pl-eyebrow">Authenticator</span>
        <span class="pl-eyebrow">Created</span><span class="pl-eyebrow">Last used</span><span></span>
      </div>

      <ul class="list-none p-0 m-0">
        @for (pk of store.passkeys(); track pk.id) {
          <li class="grid gap-2 border-b border-line-soft px-6 py-4.5 last:border-b-0
                     md:grid-cols-[minmax(200px,1.4fr)_1fr_.8fr_.8fr_auto] md:items-center md:gap-4">
            <div>
              <p class="text-body-lg font-medium">{{ pk.deviceName }}</p>
              @if (pk.isCurrentDevice) {
                <p class="mt-1 flex items-center gap-1.5 font-mono text-micro tracking-meta text-muted">
                  <span class="block h-1.5 w-1.5 bg-accent"></span>THIS DEVICE
                </p>
              } @else if (pk.aaguidLabel) {
                <p class="mt-1 font-mono text-micro tracking-meta text-faint">{{ pk.aaguidLabel }}</p>
              }
            </div>
            <div>
              <p class="text-body">{{ pk.authenticator }}</p>
              <p class="mt-0.5 font-mono text-meta text-muted">{{ pk.kind }} · {{ pk.backup }}</p>
            </div>
            <!-- md:sr-only, not md:hidden: display:none would strip the label
                 from the accessibility tree on desktop and leave a screen
                 reader announcing a bare date. -->
            <p class="font-mono text-body text-muted">
              <span class="pl-eyebrow md:sr-only">Created </span>{{ pk.createdAt }}
            </p>
            <p class="font-mono text-body text-muted">
              <span class="pl-eyebrow md:sr-only">Last used </span>{{ pk.lastUsedLabel }}
            </p>
            <button type="button" (click)="askRemove(pk)"
              class="mt-2 border border-line px-3.5 py-2 text-body hover:border-ink md:mt-0">Remove</button>
          </li>
        }
      </ul>
    </section>

    @if (pending(); as pk) {
      <div role="alertdialog" aria-modal="true" aria-labelledby="pl-remove-title"
           class="mt-6 max-w-[460px] border border-line bg-surface"
           [class.border-t-3]="store.isLastPasskey()" [class.border-t-warn]="store.isLastPasskey()">
        <div class="flex flex-col gap-3.5 p-6 pb-5">
          @if (store.isLastPasskey()) {
            <p class="pl-eyebrow text-warn-text">Security warning</p>
            <h3 id="pl-remove-title" class="text-title font-semibold">This is your last passkey</h3>
            <p class="text-body text-ink-2 text-pretty">
              Removing <span class="font-mono text-fine">{{ pk.deviceName }}</span> leaves this account
              with no passkey. You will fall back to email one-time codes, which are phishable, and you
              will be locked out entirely if you lose access to
              <span class="font-mono text-fine">rae&#64;northbound.dev</span>.
            </p>
            <label class="block">
              <span class="pl-eyebrow">Type REMOVE to confirm</span>
              <input type="text" placeholder="REMOVE" [value]="confirmText()"
                     (input)="confirmText.set($any($event.target).value)" class="pl-field mt-1.5"/>
            </label>
          } @else {
            <h3 id="pl-remove-title" class="text-title font-semibold">Remove {{ pk.deviceName }}?</h3>
            <p class="text-body text-ink-2">
              That device will no longer be able to sign in.
              {{ store.passkeys().length - 1 }} other passkeys stay active, so you keep access.
            </p>
          }
        </div>
        <div class="flex justify-end gap-2.5 border-t border-line-soft bg-sunk px-6 py-4">
          <button type="button" (click)="cancel()"
            class="border border-line px-4 py-2.75 text-body font-medium hover:border-ink">
            {{ store.isLastPasskey() ? 'Keep passkey' : 'Cancel' }}
          </button>
          @if (store.isLastPasskey()) {
            <button type="button" [disabled]="confirmText() !== 'REMOVE'" (click)="confirm(pk)"
              class="border border-warn-edge bg-warn px-4 py-2.75 text-body font-semibold text-on-warn
                     disabled:opacity-40">
              Remove anyway
            </button>
          } @else {
            <button type="button" (click)="confirm(pk)"
              class="bg-ink px-4 py-2.75 text-body font-semibold text-surface">Remove</button>
          }
        </div>
      </div>
    }
  `,
})
export class PasskeyListComponent {
  readonly store = inject(AuthStore);
  readonly pending = signal<Passkey | null>(null);
  readonly confirmText = signal('');

  askRemove(pk: Passkey): void {
    this.confirmText.set('');
    this.pending.set(pk);
  }

  cancel(): void {
    this.pending.set(null);
  }

  confirm(pk: Passkey): void {
    this.store.removePasskey(pk.id);
    this.pending.set(null);
  }
}
