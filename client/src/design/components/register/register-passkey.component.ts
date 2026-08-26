import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AuthStore } from '../../core/auth.store';

@Component({
  selector: 'pl-register-passkey',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="w-full max-w-[560px] border border-line bg-surface p-8 pb-7">
      <p class="pl-eyebrow">Step 2 of 3 · account setup</p>
      <h1 class="mt-2 text-display-lg font-semibold tracking-tight">Add a passkey to this device</h1>

      <!-- Promoted to the largest body size in the system: this is the one
           sentence a non-technical user has to read. The accent rule marks it
           as the thing to read, not as a status. -->
      <div class="mt-7 border-l-2 border-accent pl-4.5">
        <p class="text-lead text-pretty">
          A passkey lets you sign in with your fingerprint, face, or device PIN instead of a
          password — the secret never leaves your device, so there is nothing for anyone to
          steal or phish.
        </p>
        <ul class="mt-3 flex list-none flex-wrap gap-2 p-0">
          @for (claim of claims; track claim) {
            <li class="border border-line px-2.5 py-1.5 font-mono text-meta">{{ claim }}</li>
          }
        </ul>
      </div>

      <div class="mt-7 grid gap-4 sm:grid-cols-2">
        <label class="block">
          <span class="pl-eyebrow">Device name</span>
          <input type="text" [value]="deviceName()" (input)="deviceName.set($any($event.target).value)"
                 class="pl-field mt-1.5"/>
          <span class="mt-1.5 block text-caption text-faint">Shown in your passkey list. You can rename it later.</span>
        </label>
        <div>
          <span class="pl-eyebrow">Detected authenticator</span>
          <p class="mt-1.5 border border-line bg-sunk px-3 py-2.75 font-mono text-body">Touch ID · platform</p>
          <span class="mt-1.5 block text-caption text-faint">Will sync through iCloud Keychain.</span>
        </div>
      </div>

      <div class="mt-7 flex flex-col gap-3.5 border-t border-line-soft pt-5">
        <button type="button" (click)="store.authenticate()"
          class="border border-accent-edge bg-accent px-4 py-3.5 text-body-lg font-semibold text-on-accent">
          Create passkey
        </button>
        <div class="flex items-center justify-between gap-4">
          <a href="#skip" class="text-body text-muted">Skip for now, keep using a password</a>
          <span class="font-mono text-meta text-faint">ES256 · resident key</span>
        </div>
      </div>
    </section>
  `,
})
export class RegisterPasskeyComponent {
  readonly store = inject(AuthStore);
  readonly deviceName = signal("Rae's MacBook Pro");
  readonly claims = ['Nothing to remember', 'Works offline', 'Phishing-resistant'];
}
