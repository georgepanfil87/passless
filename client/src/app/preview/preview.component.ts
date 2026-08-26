import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivityLogComponent } from '../../design/components/activity/activity-log.component';
import { PasskeyListComponent } from '../../design/components/passkeys/passkey-list.component';
import { RegisterPasskeyComponent } from '../../design/components/register/register-passkey.component';
import { SessionListComponent } from '../../design/components/sessions/session-list.component';
import { SignInComponent } from '../../design/components/sign-in/sign-in.component';
import { AuthStore } from '../../design/core/auth.store';
import { ThemeService } from '../../design/core/theme.service';
import { ThemeScopeComponent } from './theme-scope.component';
import { TokenSwatchesComponent } from './token-swatches.component';

type SpecimenId = 'tokens' | 'sign-in' | 'register' | 'passkeys' | 'sessions' | 'activity';

interface Specimen {
  readonly id: SpecimenId;
  readonly title: string;
  readonly note: string;
}

/**
 * Every base component, rendered twice — once in each theme — on one page.
 *
 * Deliberately not Storybook: the whole point of the token layer is that a
 * component cannot tell which theme it is in, and the cheapest way to prove
 * that is to paint both at once from the same component instance tree. A
 * separate tool would prove it about a separate build.
 *
 * The two copies share one root-provided AuthStore, so state changes are
 * visible in both columns simultaneously — which is what makes the ceremony
 * controls below useful for comparing states across themes.
 */
@Component({
  selector: 'pl-preview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet,
    ThemeScopeComponent,
    TokenSwatchesComponent,
    SignInComponent,
    RegisterPasskeyComponent,
    PasskeyListComponent,
    SessionListComponent,
    ActivityLogComponent,
  ],
  template: `
    <ng-template #specimen let-kind="kind">
      @switch (kind) {
        @case ('tokens')   { <pl-token-swatches/> }
        @case ('sign-in')  { <pl-sign-in/> }
        @case ('register') { <pl-register-passkey/> }
        @case ('passkeys') { <pl-passkey-list/> }
        @case ('sessions') { <pl-session-list/> }
        @case ('activity') { <pl-activity-log/> }
      }
    </ng-template>

    <header class="border-b border-line px-5 py-4 sm:px-8">
      <div class="mx-auto flex max-w-[1400px] flex-wrap items-center justify-between gap-4">
        <div class="flex items-center gap-2.5">
          <span class="block h-2.5 w-2.5 bg-accent"></span>
          <span class="font-mono text-meta tracking-brand">PASSLESS · DESIGN PREVIEW</span>
        </div>

        <div class="flex flex-wrap items-center gap-2 font-mono text-meta">
          <span class="pl-eyebrow mr-1">Ceremony</span>
          <button type="button" (click)="store.reset()" [class]="control">IDLE</button>
          <button type="button" (click)="store.authenticate()" [class]="control">RUN</button>
          <button type="button" (click)="store.authenticate('unrecognised')" [class]="control">UNRECOGNISED</button>
          <button type="button" (click)="store.cancelCeremony()" [class]="control">CANCELLED</button>
          <button type="button" (click)="store.authenticate('timeout')" [class]="control">TIMEOUT</button>
          <button type="button" (click)="store.reportUnsupported()" [class]="control">UNSUPPORTED</button>
        </div>

        <button type="button" (click)="theme.toggle()" [class]="control">
          PAGE: {{ theme.theme() === 'dark' ? 'DARK' : 'LIGHT' }}
        </button>
      </div>
    </header>

    <main class="mx-auto flex max-w-[1400px] flex-col gap-10 px-5 py-10 sm:px-8">
      <p class="max-w-[64ch] text-body text-muted">
        Each specimen is one component tree painted twice, in a light scope and a dark scope.
        Nothing below sets a colour: the columns differ only by a
        <span class="font-mono text-fine text-ink">data-theme</span> attribute on their container.
      </p>

      @for (s of specimens; track s.id) {
        <article>
          <header class="flex flex-wrap items-baseline justify-between gap-3 border-b border-line pb-2.5">
            <h2 class="text-title font-semibold">{{ s.title }}</h2>
            <p class="font-mono text-meta text-faint">{{ s.note }}</p>
          </header>

          <!-- gap-px over bg-line draws the divider between the two columns
               without either column owning a border. -->
          <div class="mt-4 grid gap-px bg-line lg:grid-cols-2">
            <pl-theme-scope theme="light" class="p-6">
              <p class="pl-eyebrow mb-4">Light</p>
              <ng-container *ngTemplateOutlet="specimen; context: { kind: s.id }"/>
            </pl-theme-scope>
            <pl-theme-scope theme="dark" class="p-6">
              <p class="pl-eyebrow mb-4">Dark</p>
              <ng-container *ngTemplateOutlet="specimen; context: { kind: s.id }"/>
            </pl-theme-scope>
          </div>
        </article>
      }
    </main>
  `,
})
export class PreviewComponent {
  readonly store = inject(AuthStore);
  readonly theme = inject(ThemeService);

  readonly control =
    'border border-line px-2.5 py-1.5 font-mono text-meta uppercase tracking-meta ' +
    'text-muted hover:border-muted';

  readonly specimens: Specimen[] = [
    { id: 'tokens',   title: 'Tokens',              note: 'role → palette, per theme' },
    { id: 'sign-in',  title: 'Sign in',             note: 'idle · waiting · verifying · error · unsupported' },
    { id: 'register', title: 'Register a passkey',  note: 'the one sentence that must be read' },
    { id: 'passkeys', title: 'Passkey management',  note: 'remove · last-passkey warning' },
    { id: 'sessions', title: 'Active sessions',     note: 'revoke one · revoke all others' },
    { id: 'activity', title: 'Security activity',   note: 'info · notice · critical' },
  ];
}
