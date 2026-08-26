import { ChangeDetectionStrategy, Component } from '@angular/core';

interface Swatch {
  readonly token: string;
  readonly klass: string;
  readonly note: string;
}

/**
 * Preview aid, not a product component — it exists so the two theme anchorings
 * can be compared side by side. Every swatch paints itself with a token
 * utility, so this file contains no colour either.
 */
@Component({
  selector: 'pl-token-swatches',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="border border-line bg-surface p-5">
      <p class="pl-eyebrow">Role tokens</p>
      <ul class="mt-4 grid list-none grid-cols-2 gap-3 p-0 sm:grid-cols-3">
        @for (s of swatches; track s.token) {
          <li class="flex items-center gap-2.5">
            <span class="block h-8 w-8 shrink-0 border border-line" [class]="s.klass"></span>
            <span class="min-w-0">
              <span class="block truncate font-mono text-meta text-ink">{{ s.token }}</span>
              <span class="block truncate text-caption text-faint">{{ s.note }}</span>
            </span>
          </li>
        }
      </ul>
    </div>
  `,
})
export class TokenSwatchesComponent {
  readonly swatches: Swatch[] = [
    { token: '--pl-canvas',       klass: 'bg-canvas',       note: 'page ground' },
    { token: '--pl-surface',      klass: 'bg-surface',      note: 'cards, rows' },
    { token: '--pl-surface-sunk', klass: 'bg-sunk',         note: 'inputs, footers' },
    { token: '--pl-line',         klass: 'bg-line',         note: 'container border' },
    { token: '--pl-line-soft',    klass: 'bg-line-soft',    note: 'row divider' },
    { token: '--pl-ink',          klass: 'bg-ink',          note: 'primary text' },
    { token: '--pl-ink-2',        klass: 'bg-ink-2',        note: 'dialog body' },
    { token: '--pl-muted',        klass: 'bg-muted',        note: 'secondary text' },
    { token: '--pl-faint',        klass: 'bg-faint',        note: 'mono metadata' },
    { token: '--pl-accent',       klass: 'bg-accent',       note: 'affordance only' },
    { token: '--pl-accent-wash',  klass: 'bg-accent-wash',  note: 'ceremony halo' },
    { token: '--pl-warn',         klass: 'bg-warn',         note: 'RESERVED' },
  ];
}
