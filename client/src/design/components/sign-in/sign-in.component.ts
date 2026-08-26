import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { AuthStore } from '../../core/auth.store';

@Component({
  selector: 'pl-sign-in',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="w-full max-w-[392px] border border-line bg-surface p-8 pb-7">
      <div class="flex items-center gap-2.5">
        <span class="block h-2.5 w-2.5 bg-accent"></span>
        <span class="font-mono text-meta tracking-brand text-ink">PASSLESS</span>
      </div>

      <!-- Ceremony state is announced, not just drawn: the pause while an
           authenticator waits is silence to a screen reader otherwise. -->
      <p aria-live="polite" aria-atomic="true" class="sr-only">{{ store.liveMessage() }}</p>

      @switch (store.ceremony()) {
        @case ('waiting') {
          <div class="mt-7 flex flex-col items-center gap-5 px-2 pt-5 pb-3">
            <div class="relative flex h-22 w-22 items-center justify-center" aria-hidden="true">
              <span class="pl-breathe absolute inset-0 rounded-full border border-accent"></span>
              <span class="absolute inset-3.5 rounded-full border border-accent-edge/40"></span>
              <span class="pl-breathe-in relative flex h-11 w-11 items-center justify-center
                           rounded-full bg-accent-wash">
                <svg width="20" height="20" viewBox="0 0 20 20" fill="none">
                  <path d="M10 3c-2.6 0-4.7 2.1-4.7 4.7v1.6M10 6.6v5.2M5.3 11.6c0 2.4.6 4.2 1.5 5.9M14.7 9v3.8c0 1.9-.4 3.5-1.1 4.9M10.3 16.2c-.4-1.2-.5-2.5-.5-3.9"
                        stroke="currentColor" class="text-accent-strong" stroke-width="1.5" stroke-linecap="round"/>
                </svg>
              </span>
            </div>
            <div class="flex flex-col gap-1.5 text-center">
              <p class="text-lead font-semibold">Waiting for your authenticator</p>
              <p class="text-body text-muted">
                Touch your fingerprint sensor, or insert and tap your security key.
                Take your time — nothing expires for 60 seconds.
              </p>
            </div>
          </div>
          <div class="mt-6 flex flex-col gap-3.5 border-t border-line-soft pt-4">
            <button type="button" (click)="store.cancelCeremony()"
              class="border border-line px-4 py-3 text-body font-medium hover:border-ink">Cancel</button>
            <span class="text-center font-mono text-meta text-faint">navigator.credentials.get() pending</span>
          </div>
        }

        @case ('verifying') {
          <div class="mt-7 flex flex-col gap-5 pt-5 pb-4">
            <div class="flex items-center gap-3">
              <span class="flex h-5.5 w-5.5 items-center justify-center rounded-full bg-accent-wash" aria-hidden="true">
                <svg width="12" height="12" viewBox="0 0 12 12" fill="none">
                  <path d="M2.5 6.3l2.4 2.4 4.6-5" stroke="currentColor" class="text-accent-strong"
                        stroke-width="1.6" stroke-linecap="round"/>
                </svg>
              </span>
              <span class="text-body">Signature received from authenticator</span>
            </div>
            <p class="text-lead font-semibold">Verifying signature</p>
            <div class="relative h-0.5 overflow-hidden bg-line-soft" aria-hidden="true">
              <span class="pl-scan absolute left-0 top-0 h-0.5 w-1/3 bg-accent"></span>
            </div>
            <!-- Naming the checks makes success feel incremental rather than
                 like a spinner that might never stop. -->
            <p class="font-mono text-caption text-muted">challenge ✓ · origin ✓ · counter …</p>
          </div>
          <p class="border-t border-line-soft pt-4 font-mono text-meta text-faint">Do not close this window.</p>
        }

        @case ('unsupported') {
          <!-- No warning colour here. Missing WebAuthn support is a capability
               fact, not a threat; alarming it would devalue the real signal. -->
          <div role="status" class="mt-7 border border-line bg-sunk p-4.5">
            <p class="pl-eyebrow">No authenticator available</p>
            <p class="mt-2 text-body">
              This browser cannot create or use passkeys. {{ platformDetail() }}
            </p>
          </div>
          <div class="mt-6 flex flex-col gap-2.5">
            <button type="button" class="bg-ink px-4 py-3 text-body font-semibold text-surface">
              Email me a one-time sign-in link
            </button>
            <button type="button" class="border border-line px-4 py-3 text-body font-medium hover:border-ink">
              Sign in with a password
            </button>
          </div>
          <p class="mt-4 text-body text-muted">
            Plug in a security key and this page will detect it automatically, or continue on a
            device with Touch ID, Windows Hello, or Android biometrics.
          </p>
        }

        @default {
          <h1 class="mt-7 text-display font-semibold tracking-tight">Sign in</h1>
          <p class="mt-2 text-body text-muted">
            Use the passkey stored on this device. No password to remember or leak.
          </p>

          @if (store.ceremony() === 'error') {
            <div role="alert" class="mt-5 flex gap-3 border border-line bg-surface p-4">
              <!-- The warning colour appears on this rule only when the cause is
                   a credential the server rejected. A cancelled prompt or a
                   timeout is a neutral rule: neither implies an attacker. -->
              <span class="block w-[3px] self-stretch"
                    [class.bg-warn]="isSecurityWarning()"
                    [class.bg-line]="!isSecurityWarning()"></span>
              <div>
                <p class="pl-eyebrow" [class.text-warn-text]="isSecurityWarning()">{{ errorLabel() }}</p>
                <p class="mt-1.5 text-body">{{ store.errorMessage() }}</p>
              </div>
            </div>
          }

          <label class="mt-6 block">
            <span class="pl-eyebrow">Email</span>
            <input type="email" name="email" autocomplete="username webauthn"
                   [value]="email()" (input)="email.set($any($event.target).value)"
                   class="pl-field mt-1.5"/>
          </label>

          <div class="mt-6 flex flex-col gap-3.5">
            <button type="button" (click)="store.authenticate()"
              class="flex items-center justify-center gap-2.5 border border-accent-edge bg-accent
                     px-4 py-3.5 text-body-lg font-semibold text-on-accent">
              <svg width="15" height="15" viewBox="0 0 16 16" fill="none" aria-hidden="true">
                <path d="M8 1.5c-2.2 0-4 1.8-4 4v1.2M8 5.2v4.4M4 9.4c0 2.1.5 3.6 1.2 5.1M11.8 7.2v3.2c0 1.6-.3 3-.9 4.2M8.2 13.9c-.3-1-.4-2.1-.4-3.3"
                      stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/>
              </svg>
              Continue with passkey
            </button>
            <div class="flex items-center justify-between gap-3">
              <a href="#password" class="text-body text-muted">Use a password instead</a>
              <span class="font-mono text-meta text-faint">⏎ to submit</span>
            </div>
          </div>
        }
      }
    </section>
  `,
})
export class SignInComponent {
  readonly store = inject(AuthStore);
  readonly email = signal('rae@northbound.dev');

  readonly platformDetail = signal(
    'Firefox 114 on Linux has no platform authenticator and no USB security key was detected.'
  );

  readonly isSecurityWarning = computed(() => this.store.ceremonyError() === 'unrecognised');

  readonly errorLabel = computed(() => {
    switch (this.store.ceremonyError()) {
      case 'unrecognised': return 'Security warning';
      case 'cancelled':    return 'Ceremony cancelled';
      case 'timeout':      return 'Timed out';
      default:             return '';
    }
  });
}
